using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ICalendarSyncJobQueue
{
    /// <summary>
    /// Enqueue a sync job, coalescing against an existing Pending job for the same
    /// (userId, calendarInfoId). InProgress jobs do not suppress a new Pending job.
    /// </summary>
    Task EnqueueAsync(string userId, Guid? calendarInfoId, SyncJobSource source, string? channelId, CancellationToken ct = default);

    /// <summary>Claim the oldest eligible Pending job, marking it InProgress. Null if none.</summary>
    Task<CalendarSyncJob?> ClaimNextAsync(CancellationToken ct = default);

    Task CompleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Record a failure. retryable=true returns the job to Pending with NextAttemptAt set;
    /// retryable=false marks it terminal Failed.
    /// </summary>
    Task FailAsync(Guid id, string error, bool retryable, TimeSpan? retryAfter, CancellationToken ct = default);

    /// <summary>Reset InProgress jobs older than the threshold back to Pending (crash recovery).</summary>
    Task<int> RecoverOrphansAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Delete terminal jobs older than the retention window.</summary>
    Task<int> PruneTerminalAsync(TimeSpan olderThan, CancellationToken ct = default);

    /// <summary>Recent Failed jobs for a user, newest first (diagnostics).</summary>
    Task<IReadOnlyList<CalendarSyncJob>> GetRecentFailuresAsync(string userId, int limit, CancellationToken ct = default);
}
