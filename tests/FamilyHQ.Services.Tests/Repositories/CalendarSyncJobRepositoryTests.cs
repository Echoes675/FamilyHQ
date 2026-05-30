using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Data.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

public class CalendarSyncJobRepositoryTests : IDisposable
{
    private readonly FamilyHqDbContext _db;
    private readonly FakeTimeProvider _time;

    public CalendarSyncJobRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<FamilyHqDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new FamilyHqDbContext(options);
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose() => _db.Dispose();

    private CalendarSyncJobRepository CreateSut() => new(_db, _time);

    [Fact]
    public async Task EnqueueAsync_InsertsAPendingJob()
    {
        var sut = CreateSut();
        var cal = Guid.NewGuid();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        var jobs = await _db.CalendarSyncJobs.ToListAsync();
        jobs.Should().ContainSingle();
        jobs[0].Status.Should().Be(SyncJobStatus.Pending);
        jobs[0].CalendarInfoId.Should().Be(cal);
        jobs[0].Source.Should().Be(SyncJobSource.Webhook);
    }

    [Fact]
    public async Task EnqueueAsync_Coalesces_SecondPendingForSameTargetIsSkipped()
    {
        var sut = CreateSut();
        var cal = Guid.NewGuid();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");
        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-2");

        (await _db.CalendarSyncJobs.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_DoesNotCoalesceAgainstInProgress()
    {
        var sut = CreateSut();
        var cal = Guid.NewGuid();
        _db.CalendarSyncJobs.Add(new CalendarSyncJob
        {
            UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.InProgress,
            EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow()
        });
        await _db.SaveChangesAsync();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        // A change made mid-sync must not be lost: a new Pending job is allowed.
        (await _db.CalendarSyncJobs.CountAsync(j => j.Status == SyncJobStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_FailedJobDoesNotBlockNewEnqueue()
    {
        var sut = CreateSut();
        var cal = Guid.NewGuid();
        _db.CalendarSyncJobs.Add(new CalendarSyncJob
        {
            UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.Failed,
            EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow()
        });
        await _db.SaveChangesAsync();

        await sut.EnqueueAsync("u-1", cal, SyncJobSource.Webhook, "chan-1");

        (await _db.CalendarSyncJobs.CountAsync(j => j.Status == SyncJobStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task ClaimNextAsync_ReturnsOldestPending_AndMarksInProgress()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.AddRange(
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow().AddMinutes(-2) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow().AddMinutes(-1) });
        await _db.SaveChangesAsync();

        var claimed = await sut.ClaimNextAsync();

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(SyncJobStatus.InProgress);
        claimed.StartedAt.Should().Be(_time.GetUtcNow());
        claimed.AttemptCount.Should().Be(1);
        claimed.EnqueuedAt.Should().Be(_time.GetUtcNow().AddMinutes(-2)); // oldest first
    }

    [Fact]
    public async Task ClaimNextAsync_ReturnsNull_WhenNoEligibleJobs()
    {
        var sut = CreateSut();
        (await sut.ClaimNextAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ClaimNextAsync_SkipsJobsWithFutureNextAttemptAt()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.Add(new CalendarSyncJob
        {
            UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow(),
            NextAttemptAt = _time.GetUtcNow().AddMinutes(10) // not yet due
        });
        await _db.SaveChangesAsync();

        (await sut.ClaimNextAsync()).Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_MarksCompletedWithTimestamp()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.CompleteAsync(job.Id);

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Completed);
        reloaded.CompletedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task FailAsync_Retryable_ReturnsToPendingWithBackoff()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 1, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.FailAsync(job.Id, "boom", retryable: true, retryAfter: TimeSpan.FromSeconds(4));

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Pending);
        reloaded.NextAttemptAt.Should().Be(_time.GetUtcNow().AddSeconds(4));
        reloaded.LastError.Should().Be("boom");
    }

    [Fact]
    public async Task FailAsync_Terminal_MarksFailed()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 5, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.FailAsync(job.Id, "fatal", retryable: false, retryAfter: null);

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Failed);
        reloaded.CompletedAt.Should().Be(_time.GetUtcNow());
        reloaded.LastError.Should().Be("fatal");
    }

    [Fact]
    public async Task RecoverOrphansAsync_ResetsStaleInProgressToPending()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.AddRange(
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow().AddMinutes(-20), StartedAt = _time.GetUtcNow().AddMinutes(-20) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() });
        await _db.SaveChangesAsync();

        var recovered = await sut.RecoverOrphansAsync(TimeSpan.FromMinutes(5));

        recovered.Should().Be(1);
        (await _db.CalendarSyncJobs.CountAsync(j => j.Status == SyncJobStatus.Pending)).Should().Be(1);
    }

    [Fact]
    public async Task PruneTerminalAsync_DeletesOldCompletedAndFailed_KeepsRecentAndActive()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.AddRange(
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow().AddDays(-10), CompletedAt = _time.GetUtcNow().AddDays(-10) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed,    EnqueuedAt = _time.GetUtcNow().AddDays(-10), CompletedAt = _time.GetUtcNow().AddDays(-10) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow().AddDays(-1),  CompletedAt = _time.GetUtcNow().AddDays(-1) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending,   EnqueuedAt = _time.GetUtcNow() });
        await _db.SaveChangesAsync();

        var pruned = await sut.PruneTerminalAsync(TimeSpan.FromDays(7));

        pruned.Should().Be(2);
        (await _db.CalendarSyncJobs.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetRecentFailuresAsync_ReturnsOnlyFailedForUser_NewestFirst()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.AddRange(
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddMinutes(-2) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow().AddMinutes(-1) },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "other", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() });
        await _db.SaveChangesAsync();

        var result = await sut.GetRecentFailuresAsync("u", limit: 10);

        result.Should().HaveCount(2);
        result[0].CompletedAt.Should().Be(_time.GetUtcNow().AddMinutes(-1)); // newest first
    }

    [Fact]
    public async Task FailAsync_Terminal_ClearsNextAttemptAt()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob
        {
            UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 5,
            EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow(),
            NextAttemptAt = _time.GetUtcNow().AddSeconds(30) // stale backoff from a prior retry
        };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.FailAsync(job.Id, "fatal", retryable: false, retryAfter: null);

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Failed);
        reloaded.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task FailAsync_Retryable_WithNullRetryAfter_IsImmediatelyEligible()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, AttemptCount = 1, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.FailAsync(job.Id, "boom", retryable: true, retryAfter: null);

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Pending);
        reloaded.NextAttemptAt.Should().Be(_time.GetUtcNow()); // now + Zero
    }

    [Fact]
    public async Task RecoverOrphansAsync_ClearsStartedBackoffOnRecoveredJob()
    {
        var sut = CreateSut();
        var job = new CalendarSyncJob
        {
            UserId = "u", Status = SyncJobStatus.InProgress,
            EnqueuedAt = _time.GetUtcNow().AddMinutes(-20), StartedAt = _time.GetUtcNow().AddMinutes(-20),
            NextAttemptAt = _time.GetUtcNow().AddMinutes(-15)
        };
        _db.CalendarSyncJobs.Add(job);
        await _db.SaveChangesAsync();

        await sut.RecoverOrphansAsync(TimeSpan.FromMinutes(5));

        var reloaded = await _db.CalendarSyncJobs.FindAsync(job.Id);
        reloaded!.Status.Should().Be(SyncJobStatus.Pending);
        reloaded.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task EnqueueAsync_Throws_WhenUserIdEmpty()
    {
        var sut = CreateSut();
        var act = async () => await sut.EnqueueAsync("", Guid.NewGuid(), SyncJobSource.Webhook, null);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetActiveJobCountAsync_CountsOnlyPendingAndInProgressForUser()
    {
        var sut = CreateSut();
        _db.CalendarSyncJobs.AddRange(
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = _time.GetUtcNow(), StartedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = _time.GetUtcNow(), CompletedAt = _time.GetUtcNow() },
            new CalendarSyncJob { UserId = "other", Status = SyncJobStatus.Pending, EnqueuedAt = _time.GetUtcNow() });
        await _db.SaveChangesAsync();

        (await sut.GetActiveJobCountAsync("u")).Should().Be(2);
    }

    [Fact]
    public async Task GetActiveJobCountAsync_ReturnsZero_ForEmptyUserId()
    {
        var sut = CreateSut();
        (await sut.GetActiveJobCountAsync("")).Should().Be(0);
    }
}
