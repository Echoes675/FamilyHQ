using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Data.Repositories;

public class CalendarSyncJobRepository(
    FamilyHqDbContext context,
    TimeProvider timeProvider,
    ISyncJobClaimStore claimStore,
    ILogger<CalendarSyncJobRepository> logger) : ICalendarSyncJobQueue
{
    private const int MaxErrorLength = 1000;

    /// <summary>
    /// Bound on claim retries. Each retry means a competing worker won the previous row; re-querying
    /// moves past it. Prevents livelock under heavy contention. Never reached with a single worker.
    /// </summary>
    private const int MaxClaimAttempts = 5;

    public async Task EnqueueAsync(string userId, Guid? calendarInfoId, SyncJobSource source, string? channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("userId must not be empty.", nameof(userId));

        // FHQ-69: coalesce only within the same work type (see SyncJobSourceExtensions.IsReconcileOnly,
        // the single source of truth shared with the worker's dispatch branch). The worker executes
        // DesignationChange jobs as reconcile-only (placement reconciler, no Google sync) and every
        // other source as a Google sync (reconciling only when the sync changed data), so coalescing
        // across the two work types silently drops work: a pending Periodic/Login sync-all must not
        // swallow a DesignationChange (the reconcile would never run for that cycle), and a pending
        // DesignationChange must not swallow a sync (no Google sync would run). Sync-type sources
        // (Webhook/Periodic/Login) still coalesce with each other — the worker does identical work
        // for them.
        //
        // The lambda compares j.Source against the DesignationChange literal (the DB-side encoding of
        // IsReconcileOnly for the single reconcile-only member) rather than calling the helper: an
        // extension-method call inside the expression tree would not translate to SQL.
        var enqueueIsReconcileOnly = source.IsReconcileOnly();
        var alreadyPending = await context.CalendarSyncJobs.AnyAsync(
            j => j.UserId == userId
                 && j.CalendarInfoId == calendarInfoId
                 && j.Status == SyncJobStatus.Pending
                 && (j.Source == SyncJobSource.DesignationChange) == enqueueIsReconcileOnly,
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
        catch (UniqueConstraintException)
        {
            context.ChangeTracker.Clear();
        }
    }

    public async Task<CalendarSyncJob?> ClaimNextAsync(CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
        {
            var job = await claimStore.FindNextClaimableAsync(ct);
            if (job is null) return null;                        // queue empty

            if (await claimStore.TryClaimAsync(job, ct)) return job;  // claimed
            // else a competing worker claimed this row first — re-query the next eligible job.
        }

        logger.LogWarning(
            "ClaimNextAsync exhausted {Attempts} claim attempts under contention; yielding this cycle.",
            MaxClaimAttempts);
        return null;
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
            job.NextAttemptAt = null;
        }

        await context.SaveChangesAsync(ct);
    }
    public async Task<int> RecoverOrphansAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow() - olderThan;
        var stuck = await context.CalendarSyncJobs
            .Where(j => j.Status == SyncJobStatus.InProgress && j.StartedAt != null && j.StartedAt < cutoff)
            .ToListAsync(ct);

        foreach (var job in stuck)
        {
            job.Status = SyncJobStatus.Pending;
            job.NextAttemptAt = null;
        }

        if (stuck.Count > 0)
            await context.SaveChangesAsync(ct);

        return stuck.Count;
    }

    public async Task<int> PruneTerminalAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        var cutoff = timeProvider.GetUtcNow() - olderThan;
        var old = await context.CalendarSyncJobs
            .Where(j => (j.Status == SyncJobStatus.Completed || j.Status == SyncJobStatus.Failed)
                        && j.CompletedAt != null && j.CompletedAt < cutoff)
            .ToListAsync(ct);

        if (old.Count > 0)
        {
            context.CalendarSyncJobs.RemoveRange(old);
            await context.SaveChangesAsync(ct);
        }

        return old.Count;
    }

    public async Task<IReadOnlyList<CalendarSyncJob>> GetRecentFailuresAsync(string userId, int limit, TimeSpan maxAge, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return Array.Empty<CalendarSyncJob>();

        var cutoff = timeProvider.GetUtcNow() - maxAge;

        return await context.CalendarSyncJobs
            .AsNoTracking()
            .Where(j => j.UserId == userId && j.Status == SyncJobStatus.Failed && j.CompletedAt != null && j.CompletedAt >= cutoff)
            .OrderByDescending(j => j.CompletedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> GetActiveJobCountAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId))
            return 0;

        return await context.CalendarSyncJobs
            .AsNoTracking()
            .CountAsync(
                j => j.UserId == userId
                     && (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.InProgress),
                ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
