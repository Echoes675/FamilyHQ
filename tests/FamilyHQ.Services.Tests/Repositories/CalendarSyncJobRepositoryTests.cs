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
}
