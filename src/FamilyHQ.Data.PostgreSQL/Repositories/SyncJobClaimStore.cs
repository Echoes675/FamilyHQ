using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

/// <summary>
/// EF-backed <see cref="ISyncJobClaimStore"/>. Thin mechanism: the atomicity comes from the
/// <c>xmin</c> optimistic-concurrency token on <see cref="CalendarSyncJob"/> (see
/// <c>NpgsqlModelCustomizer</c>), which raises <see cref="DbUpdateConcurrencyException"/> when a
/// competing worker has already claimed the row. Not unit-tested (would require a real/in-memory
/// database); exercised against real Postgres on Deploy-Dev.
/// </summary>
public class SyncJobClaimStore(FamilyHqDbContext context, TimeProvider timeProvider) : ISyncJobClaimStore
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
            // Another worker claimed this row first (xmin mismatch). Discard our tracked change so
            // the next FindNextClaimableAsync re-reads fresh state (mirrors EnqueueAsync's clear-on-conflict).
            context.ChangeTracker.Clear();
            return false;
        }
    }
}
