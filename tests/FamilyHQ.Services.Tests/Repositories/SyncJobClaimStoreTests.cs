using FamilyHQ.Core.Models;
using FamilyHQ.Data.Repositories;
using FamilyHQ.Services.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace FamilyHQ.Services.Tests.Repositories;

/// <summary>
/// Pure unit tests for <see cref="SyncJobClaimStore.FindNextClaimableAsync"/> (FHQ-146 final review).
/// The query is a plain <c>Where(...).OrderBy(...).FirstOrDefaultAsync()</c> — it touches neither the
/// <c>xmin</c> concurrency token nor any provider-specific behaviour, so it runs against the
/// provider-free <see cref="FakeFamilyHqDbContext"/> exactly like the other repository query tests.
/// <see cref="SyncJobClaimStore.TryClaimAsync"/> is deliberately NOT covered here: its success path
/// mutates the entity ahead of <c>SaveChangesAsync</c>, and its contention path relies on the real
/// <c>xmin</c>-token-driven <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>,
/// which genuinely needs a real database provider to raise — that behaviour is exercised against real
/// Postgres on Deploy-Dev, per <see cref="SyncJobClaimStore"/>'s own class doc comment.
/// </summary>
public class SyncJobClaimStoreTests
{
    private readonly FakeFamilyHqDbContext _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero));

    private SyncJobClaimStore CreateSut() => new(_db, _time, NullLogger<SyncJobClaimStore>.Instance);

    private static CalendarSyncJob PendingJob(DateTimeOffset enqueuedAt, DateTimeOffset? nextAttemptAt = null) => new()
    {
        UserId = "u",
        Status = SyncJobStatus.Pending,
        EnqueuedAt = enqueuedAt,
        NextAttemptAt = nextAttemptAt
    };

    [Fact]
    public async Task FindNextClaimableAsync_ReturnsOldestPendingJob_WhenSeveralAreEligible()
    {
        var now = _time.GetUtcNow();
        var oldest = PendingJob(now.AddMinutes(-30));
        var middle = PendingJob(now.AddMinutes(-20));
        var newest = PendingJob(now.AddMinutes(-10));
        // Seeded out of chronological order so the assertion actually exercises OrderBy(EnqueuedAt).
        _db.Setup<CalendarSyncJob>([newest, oldest, middle]);
        var sut = CreateSut();

        var claimable = await sut.FindNextClaimableAsync();

        claimable.Should().NotBeNull();
        claimable!.Id.Should().Be(oldest.Id);
    }

    [Fact]
    public async Task FindNextClaimableAsync_FiltersByNextAttemptAt_RelativeToClock()
    {
        var sut = CreateSut();
        var now = _time.GetUtcNow();

        // Only a future-NextAttemptAt job exists: nothing is eligible yet.
        var futureOnly = PendingJob(now.AddMinutes(-10), nextAttemptAt: now.AddMinutes(5));
        _db.Setup<CalendarSyncJob>([futureOnly]);
        (await sut.FindNextClaimableAsync()).Should().BeNull();

        // A future job coexists with a NextAttemptAt == null eligible job: the eligible one wins.
        var eligibleNull = PendingJob(now.AddMinutes(-5), nextAttemptAt: null);
        _db.Setup<CalendarSyncJob>([futureOnly, eligibleNull]);
        var resultNull = await sut.FindNextClaimableAsync();
        resultNull.Should().NotBeNull();
        resultNull!.Id.Should().Be(eligibleNull.Id);

        // A future job coexists with a NextAttemptAt <= now eligible job: the eligible one wins.
        var eligiblePast = PendingJob(now.AddMinutes(-3), nextAttemptAt: now.AddMinutes(-1));
        _db.Setup<CalendarSyncJob>([futureOnly, eligiblePast]);
        var resultPast = await sut.FindNextClaimableAsync();
        resultPast.Should().NotBeNull();
        resultPast!.Id.Should().Be(eligiblePast.Id);
    }

    [Fact]
    public async Task FindNextClaimableAsync_ReturnsNull_WhenNoEligibleJobExists()
    {
        var sut = CreateSut();
        var now = _time.GetUtcNow();

        // Empty set.
        _db.Setup<CalendarSyncJob>();
        (await sut.FindNextClaimableAsync()).Should().BeNull();

        // Only non-Pending statuses — none of these are eligible regardless of NextAttemptAt.
        var inProgress = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.InProgress, EnqueuedAt = now, StartedAt = now };
        var completed = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Completed, EnqueuedAt = now, CompletedAt = now };
        var failed = new CalendarSyncJob { UserId = "u", Status = SyncJobStatus.Failed, EnqueuedAt = now, CompletedAt = now };
        _db.Setup<CalendarSyncJob>([inProgress, completed, failed]);
        (await sut.FindNextClaimableAsync()).Should().BeNull();
    }
}
