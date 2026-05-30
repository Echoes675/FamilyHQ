namespace FamilyHQ.Core.Models;

/// <summary>
/// Outcome of a calendar sync run. <see cref="ChangedCount"/> is the number of material
/// event/calendar mutations persisted; it deliberately excludes the per-sync
/// <c>SyncState</c> bookkeeping write (which happens on every sync, even a no-op).
/// </summary>
public record SyncResult(int ChangedCount)
{
    public bool HadChanges => ChangedCount > 0;
}
