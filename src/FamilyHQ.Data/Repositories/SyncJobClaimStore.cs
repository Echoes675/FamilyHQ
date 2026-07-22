using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Data.Repositories;

/// <summary>
/// EF-backed <see cref="ISyncJobClaimStore"/>. Thin mechanism: the atomicity comes from the
/// <c>xmin</c> optimistic-concurrency token on <see cref="CalendarSyncJob"/>, which raises
/// <see cref="DbUpdateConcurrencyException"/> when a competing worker has already claimed the row.
/// Not unit-tested (would require a real/in-memory database); exercised against real Postgres on
/// Deploy-Dev.
/// </summary>
public class SyncJobClaimStore(
    FamilyHqDbContext context,
    TimeProvider timeProvider,
    ILogger<SyncJobClaimStore> logger) : ISyncJobClaimStore
{
    public async Task<CalendarSyncJob?> FindNextClaimableAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        return await context.CalendarSyncJobs
            .Where(j => j.Status == SyncJobStatus.Pending
                        && (j.NextAttemptAt == null || j.NextAttemptAt <= now))
            .OrderBy(j => j.EnqueuedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryClaimAsync(CalendarSyncJob job, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        // Mutating `job` before the save is safe on the losing path: ChangeTracker.Clear() detaches it and
        // ClaimNextAsync only returns the instance when this method reports success.
        job.Status = SyncJobStatus.InProgress;
        job.StartedAt = now;
        job.AttemptCount += 1;

        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker claimed this row first (xmin mismatch). Benign and expected under contention.
            // ChangeTracker.Clear() is scope-wide: it is only safe because this store runs inside
            // CalendarSyncWorker.DrainAsync's per-job scope, where the claim is the only tracked change.
            // Clearing lets the next FindNextClaimableAsync re-read fresh state instead of resolving the
            // stale, still-tracked instance (mirrors EnqueueAsync's clear-on-conflict).
            logger.LogDebug("Sync job {JobId} was claimed by a competing worker; re-querying the queue.", job.Id);
            context.ChangeTracker.Clear();
            return false;
        }
    }
}
