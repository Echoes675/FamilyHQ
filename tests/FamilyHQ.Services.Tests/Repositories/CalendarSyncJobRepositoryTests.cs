using FamilyHQ.Core.Models;
using FamilyHQ.Data.Exceptions;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

/// <summary>
/// Pure unit tests for <see cref="CalendarSyncJobRepository"/> against the provider-free
/// <see cref="FakeFamilyHqDbContext"/> (FHQ-146) — no InMemory provider, no real DB. Writes do not
/// round-trip on the double: insert paths are asserted by interaction (Add + SaveChanges), update-in-place
/// paths are asserted on the seeded instance the mock returns.
/// <see cref="CalendarSyncJobRepository.ClaimNextAsync"/> delegates entirely to
/// <see cref="ISyncJobClaimStore"/>, which is mocked here rather than exercised through real EF (its
/// retry/contention policy already has pure coverage in <see cref="CalendarSyncJobClaimPolicyTests"/>).
/// </summary>
public class CalendarSyncJobRepositoryTests
{
    private readonly FakeFamilyHqDbContext _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero));

    private CalendarSyncJobRepository CreateSut() =>
        new(_db, _time, new SyncJobClaimStore(_db, _time, NullLogger<SyncJobClaimStore>.Instance), NullLogger<CalendarSyncJobRepository>.Instance);

    private CalendarSyncJobRepository CreateSutWithMockClaimStore(ISyncJobClaimStore claimStore) =>
        new(_db, _time, claimStore, NullLogger<CalendarSyncJobRepository>.Instance);

    [Fact]
    public async Task EnqueueAsync_InsertsAPendingJob()
    {
        var mockSet = _db.Setup<CalendarSyncJob>();
        var sut = CreateSut();
        var cal = Guid.NewGuid();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        mockSet.Verify(s => s.Add(It.Is<CalendarSyncJob>(j =>
            j.Status == SyncJobStatus.Pending &&
            j.CalendarInfoId == cal &&
            j.Source == SyncJobSource.Webhook &&
            j.ChannelId == "chan-1")), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_Coalesces_SecondPendingForSameTargetIsSkipped()
    {
        // A Pending job already exists for this user+target — this is exactly the state AnyAsync
        // detects in the coalesce guard, so EnqueueAsync must no-op rather than insert a second one.
        var cal = Guid.NewGuid();
        var existing = new CalendarSyncJob
        {
            UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow()
        };
        var mockSet = _db.Setup<CalendarSyncJob>([existing]);
        var sut = CreateSut();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-2");

        mockSet.Verify(s => s.Add(It.IsAny<CalendarSyncJob>()), Times.Never);
        _db.SaveChangesCount.Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotCoalesceAgainstInProgress()
    {
        var cal = Guid.NewGuid();
        var existing = new CalendarSyncJob
        {
            UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.InProgress,
            EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow()
        };
        var mockSet = _db.Setup<CalendarSyncJob>([existing]);
        var sut = CreateSut();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        // A change made mid-sync must not be lost: a new Pending job is allowed.
        mockSet.Verify(s => s.Add(It.Is<CalendarSyncJob>(j => j.Status == SyncJobStatus.Pending)), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_FailedJobDoesNotBlockNewEnqueue()
    {
        var cal = Guid.NewGuid();
        var existing = new CalendarSyncJob
        {
            UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.Failed,
            EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow()
        };
        var mockSet = _db.Setup<CalendarSyncJob>([existing]);
        var sut = CreateSut();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        mockSet.Verify(s => s.Add(It.Is<CalendarSyncJob>(j => j.Status == SyncJobStatus.Pending)), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_Throws_WhenUserIdEmpty()
    {
        var sut = CreateSut();
        var act = async () => await sut.EnqueueAsync("", Guid.NewGuid(), SyncJobSource.Webhook, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // --- ClaimNextAsync ---
    //
    // ClaimNextAsync (the repository method) does nothing but delegate to ISyncJobClaimStore and retry
    // on a lost race — that retry/contention policy is already fully covered purely by
    // CalendarSyncJobClaimPolicyTests using a mocked store, so it is not duplicated here. The ordering
    // (oldest-eligible-first) and NextAttemptAt backoff filtering the *previous* version of this test
    // class exercised live inside SyncJobClaimStore.FindNextClaimableAsync's EF query — a different type,
    // not covered by ClaimPolicyTests, and (per the FHQ-146 brief) not to be exercised through real EF
    // here. That query-level coverage has no purely-tested home after this migration; see
    // task-7-report.md for the concern raised to the controller. The single test kept below proves the
    // repository still returns whatever the store claims (including the store's own mutation of the job).
    [Fact]
    public async Task ClaimNextAsync_WhenStoreClaimsSuccessfully_ReturnsTheClaimedJob()
    {
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow() };
        var claimStore = new Mock<ISyncJobClaimStore>(MockBehavior.Strict);
        claimStore.Setup(s => s.FindNextClaimableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(job);
        claimStore.Setup(s => s.TryClaimAsync(job, It.IsAny<CancellationToken>()))
            .Callback<CalendarSyncJob, CancellationToken>((j, _) =>
            {
                j.Status = SyncJobStatus.InProgress;
                j.StartedAt = _time.GetUtcNow();
                j.AttemptCount += 1;
            })
            .ReturnsAsync(true);
        var sut = CreateSutWithMockClaimStore(claimStore.Object);

        var claimed = await sut.ClaimNextAsync();

        claimed.Should().BeSameAs(job);
        claimed!.Status.Should().Be(SyncJobStatus.InProgress);
        claimed.StartedAt.Should().Be(_time.GetUtcNow());
        claimed.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task CompleteAsync_MarksCompletedWithTimestamp()
    {
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.CompleteAsync(job.Id);

        job.Status.Should().Be(SyncJobStatus.Completed);
        job.CompletedAt.Should().Be(_time.GetUtcNow());
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task FailAsync_Retryable_ReturnsToPendingWithBackoff()
    {
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 1, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.FailAsync(job.Id, "boom", retryable: true, retryAfter: TimeSpan.FromSeconds(4));

        job.Status.Should().Be(SyncJobStatus.Pending);
        job.NextAttemptAt.Should().Be(_time.GetUtcNow().AddSeconds(4));
        job.LastError.Should().Be("boom");
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task FailAsync_Terminal_MarksFailed()
    {
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 5, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.FailAsync(job.Id, "fatal", retryable: false, retryAfter: null);

        job.Status.Should().Be(SyncJobStatus.Failed);
        job.CompletedAt.Should().Be(_time.GetUtcNow());
        job.LastError.Should().Be("fatal");
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task FailAsync_Terminal_ClearsNextAttemptAt()
    {
        var job = new CalendarSyncJob
        {
            UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 5,
            EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow(),
            NextAttemptAt = _time.GetUtcNow().AddSeconds(30) // stale backoff from a prior retry
        };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.FailAsync(job.Id, "fatal", retryable: false, retryAfter: null);

        job.Status.Should().Be(SyncJobStatus.Failed);
        job.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task FailAsync_Retryable_WithNullRetryAfter_IsImmediatelyEligible()
    {
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 1, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.FailAsync(job.Id, "boom", retryable: true, retryAfter: null);

        job.Status.Should().Be(SyncJobStatus.Pending);
        job.NextAttemptAt.Should().Be(_time.GetUtcNow()); // now + Zero
    }

    [Fact]
    public async Task RecoverOrphansAsync_ResetsStaleInProgressToPending()
    {
        var stale = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow().AddMinutes(-20), StartedAt = _time.GetUtcNow().AddMinutes(-20) };
        var fresh = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([stale, fresh]);
        var sut = CreateSut();

        var recovered = await sut.RecoverOrphansAsync(TimeSpan.FromMinutes(5));

        recovered.Should().Be(1);
        stale.Status.Should().Be(SyncJobStatus.Pending);
        fresh.Status.Should().Be(SyncJobStatus.InProgress); // untouched — not stale enough
    }

    [Fact]
    public async Task RecoverOrphansAsync_ClearsStartedBackoffOnRecoveredJob()
    {
        var job = new CalendarSyncJob
        {
            UserId = "u", Status = SyncJobStatus.InProgress,
            EnqueuedAt = _time.GetUtcNow().AddMinutes(-20), StartedAt = _time.GetUtcNow().AddMinutes(-20),
            NextAttemptAt = _time.GetUtcNow().AddMinutes(-15)
        };
        _db.Setup<CalendarSyncJob>([job]);
        var sut = CreateSut();

        await sut.RecoverOrphansAsync(TimeSpan.FromMinutes(5));

        job.Status.Should().Be(SyncJobStatus.Pending);
        job.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task PruneTerminalAsync_DeletesOldCompletedAndFailed_KeepsRecentAndActive()
    {
        var oldCompleted = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow().AddDays(-10), CompletedAt = _time.GetUtcNow().AddDays(-10) };
        var oldFailed    = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed,    EnqueuedAt = _time.GetUtcNow().AddDays(-10), CompletedAt = _time.GetUtcNow().AddDays(-10) };
        var recent       = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow().AddDays(-1),  CompletedAt = _time.GetUtcNow().AddDays(-1) };
        var active       = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending,   EnqueuedAt = _time.GetUtcNow() };
        var mockSet = _db.Setup<CalendarSyncJob>([oldCompleted, oldFailed, recent, active]);
        var sut = CreateSut();

        var pruned = await sut.PruneTerminalAsync(TimeSpan.FromDays(7));

        // The mock doesn't reflect RemoveRange on later reads, so assert the returned count and the
        // RemoveRange interaction rather than re-querying the set.
        pruned.Should().Be(2);
        mockSet.Verify(s => s.RemoveRange(It.Is<IEnumerable<CalendarSyncJob>>(rows =>
            rows.Contains(oldCompleted) && rows.Contains(oldFailed) &&
            !rows.Contains(recent) && !rows.Contains(active))), Times.Once);
        _db.SaveChangesCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRecentFailuresAsync_ReturnsOnlyFailedForUser_NewestFirst()
    {
        var older = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddMinutes(-2) };
        var newer = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddMinutes(-1) };
        var completed = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() };
        var otherUser = new CalendarSyncJob { UserId = "other", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() };
        _db.Setup<CalendarSyncJob>([older, newer, completed, otherUser]);
        var sut = CreateSut();

        var result = await sut.GetRecentFailuresAsync("u", limit: 10, maxAge: TimeSpan.FromDays(14));

        result.Should().HaveCount(2);
        result[0].CompletedAt.Should().Be(_time.GetUtcNow().AddMinutes(-1)); // newest first
    }

    [Fact]
    public async Task GetRecentFailuresAsync_ExcludesFailuresOlderThanMaxAge()
    {
        var withinWindow = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddDays(-1) };
        var tooOld = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddDays(-20) };
        _db.Setup<CalendarSyncJob>([withinWindow, tooOld]);
        var sut = CreateSut();

        var result = await sut.GetRecentFailuresAsync("u", limit: 10, maxAge: TimeSpan.FromDays(14));

        result.Should().ContainSingle();
        result[0].CompletedAt.Should().Be(_time.GetUtcNow().AddDays(-1));
    }

    [Fact]
    public async Task GetRecentFailuresAsync_CombinesAgeFilterWithLimit_ReturningNewestWithinWindow()
    {
        var newest = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() };
        var withinWindow = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddDays(-1) };
        var tooOld = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddDays(-20) };
        _db.Setup<CalendarSyncJob>([newest, withinWindow, tooOld]);
        var sut = CreateSut();

        var result = await sut.GetRecentFailuresAsync("u", limit: 1, maxAge: TimeSpan.FromDays(14));

        result.Should().ContainSingle();
        result[0].CompletedAt.Should().Be(_time.GetUtcNow()); // newest of the 2 within-window rows
    }

    [Fact]
    public async Task GetActiveJobCountAsync_CountsOnlyPendingAndInProgressForUser()
    {
        _db.Setup<CalendarSyncJob>([
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "other", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow() }
        ]);
        var sut = CreateSut();

        (await sut.GetActiveJobCountAsync("u")).Should().Be(2);
    }

    [Fact]
    public async Task GetActiveJobCountAsync_ReturnsZero_ForEmptyUserId()
    {
        var sut = CreateSut();
        (await sut.GetActiveJobCountAsync("")).Should().Be(0);
    }

    // --- EnqueueAsync save-failure paths ---

    [Fact]
    public async Task EnqueueAsync_NonUniqueDbUpdateException_Rethrows()
    {
        // A plain DbUpdateException (not the UniqueConstraintException subtype) is never caught by
        // EnqueueAsync's `catch (UniqueConstraintException)` — it propagates directly, never touching
        // ChangeTracker, so this migrates cleanly onto the provider-free double.
        _db.Setup<CalendarSyncJob>();
        _db.OnSaveChanges = () => throw new DbUpdateException("transient DB error", new Exception("connection refused"));
        var sut = CreateSut();

        var act = async () => await sut.EnqueueAsync("u-1", Guid.NewGuid(), SyncJobSource.Webhook, null);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// KNOWN CONCERN (FHQ-146 Task 7 — see task-7-report.md): EnqueueAsync's swallow of
    /// <see cref="UniqueConstraintException"/> calls <c>context.ChangeTracker.Clear()</c>. On
    /// <see cref="FakeFamilyHqDbContext"/> — deliberately provider-free so every other test in this
    /// suite stays pure — <c>ChangeTracker</c> access forces EF's internal service provider to
    /// initialize, which throws because no provider is configured. That happens inside the SUT's own
    /// catch block, so it is not possible on this double to observe "the exception is swallowed and
    /// EnqueueAsync does not throw" the way the original InMemory-backed test did. The only thing
    /// observable purely is that SaveChangesAsync was attempted before the (now different) exception
    /// propagates from the ChangeTracker access. This is a real fidelity gap versus production
    /// behaviour, not a simplification — flagged for the controller to decide whether to harden the
    /// fake (e.g. an overridable ChangeTracker) or accept this residual coverage.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_UniqueConstraintException_AttemptsSaveChanges_SwallowNotObservableOnTheDouble()
    {
        _db.Setup<CalendarSyncJob>();
        var original = new DbUpdateException("race", new Exception("inner"));
        _db.OnSaveChanges = () => throw new UniqueConstraintException("race", original);
        var sut = CreateSut();

        try
        {
            await sut.EnqueueAsync("u-1", Guid.NewGuid(), SyncJobSource.Webhook, null);
        }
        catch (InvalidOperationException)
        {
            // Expected on this double: see the KNOWN CONCERN doc comment above — provider-less
            // ChangeTracker access throws InvalidOperationException. A different exception here would
            // mean the fidelity gap changed shape, so it is intentionally not swallowed.
        }

        _db.SaveChangesCount.Should().Be(1);
    }
}
