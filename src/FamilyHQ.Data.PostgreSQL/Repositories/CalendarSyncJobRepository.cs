using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class CalendarSyncJobRepository(FamilyHqDbContext context, TimeProvider timeProvider) : ICalendarSyncJobQueue
{
    private const int MaxErrorLength = 1000;

    public async Task EnqueueAsync(string userId, Guid? calendarInfoId, SyncJobSource source, string? channelId, CancellationToken ct = default)
    {
        var alreadyPending = await context.CalendarSyncJobs.AnyAsync(
            j => j.UserId == userId
                 && j.CalendarInfoId == calendarInfoId
                 && j.Status == SyncJobStatus.Pending,
            ct);
        if (alreadyPending) return;

        context.CalendarSyncJobs.Add(new CalendarSyncJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CalendarInfoId = calendarInfoId,
            Status = SyncJobStatus.Pending,
            Source = source,
            ChannelId = channelId,
            AttemptCount = 0,
            EnqueuedAt = timeProvider.GetUtcNow()
        });

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique-index backstop: a concurrent enqueue created the Pending job first.
            context.ChangeTracker.Clear();
        }
    }

    public async Task<CalendarSyncJob?> ClaimNextAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        var job = await context.CalendarSyncJobs
            .Where(j => j.Status == SyncJobStatus.Pending
                        && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.EnqueuedAt)
            .FirstOrDefaultAsync(ct);

        if (job is null) return null;

        job.Status = SyncJobStatus.InProgress;
        job.StartedAt = now;
        job.AttemptCount += 1;
        await context.SaveChangesAsync(ct);
        return job;
    }
    public async Task CompleteAsync(Guid id, CancellationToken ct = default)
    {
        var job = await context.CalendarSyncJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return;

        job.Status = SyncJobStatus.Completed;
        job.CompletedAt = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(ct);
    }

    public async Task FailAsync(Guid id, string error, bool retryable, TimeSpan? retryAfter, CancellationToken ct = default)
    {
        var job = await context.CalendarSyncJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return;

        var now = timeProvider.GetUtcNow();
        job.LastError = Truncate(error, MaxErrorLength);

        if (retryable)
        {
            job.Status = SyncJobStatus.Pending;
            job.NextAttemptAt = now + (retryAfter ?? TimeSpan.Zero);
        }
        else
        {
            job.Status = SyncJobStatus.Failed;
            job.CompletedAt = now;
        }

        await context.SaveChangesAsync(ct);
    }
    public Task<int> RecoverOrphansAsync(TimeSpan olderThan, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> PruneTerminalAsync(TimeSpan olderThan, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<CalendarSyncJob>> GetRecentFailuresAsync(string userId, int limit, CancellationToken ct = default) => throw new NotImplementedException();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
