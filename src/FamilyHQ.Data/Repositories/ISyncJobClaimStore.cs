using FamilyHQ.Core.Models;

namespace FamilyHQ.Data.Repositories;

/// <summary>
/// The two persistence operations that make up an atomic sync-job claim, isolated behind an
/// interface so the retry <em>policy</em> in <see cref="CalendarSyncJobRepository.ClaimNextAsync"/>
/// can be unit-tested without a database. The EF <em>mechanism</em> lives in
/// <see cref="SyncJobClaimStore"/> and is verified against real Postgres on Deploy-Dev.
/// </summary>
public interface ISyncJobClaimStore
{
    /// <summary>
    /// The oldest eligible <c>Pending</c> job (respecting <c>NextAttemptAt</c> backoff), tracked so it
    /// can be claimed. Returns <c>null</c> when the queue is empty.
    /// </summary>
    Task<CalendarSyncJob?> FindNextClaimableAsync(CancellationToken ct = default);

    /// <summary>
    /// Attempts to claim <paramref name="job"/> (flip to <c>InProgress</c> and persist).
    /// Returns <c>true</c> when claimed; <c>false</c> when a competing worker claimed it first
    /// (optimistic-concurrency conflict).
    /// </summary>
    Task<bool> TryClaimAsync(CalendarSyncJob job, CancellationToken ct = default);
}
