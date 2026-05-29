using System.Collections.Concurrent;

namespace FamilyHQ.Simulator.State;

/// <summary>
/// In-memory write counter keyed by Google event ID. Incremented by the
/// EventsController on every PUT (update) call — used by E2E backdoor assertions
/// to verify that exactly one outbound write occurred for a given event.
/// Reset via the backdoor endpoint between scenarios.
/// </summary>
public sealed class OutboundWriteCountStore
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    // Per-user totals. Used by recurring echo-guard assertions where the written event's ID is
    // generated server-side (native series creation), so a per-ID count isn't usable. A GLOBAL
    // total is unsafe under the parallel E2E runner + shared Simulator — a concurrent scenario's
    // writes inflate it. Keying by the scenario's isolated user keeps the count uncontaminated.
    private readonly ConcurrentDictionary<string, int> _userTotals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Increments the write count for the given event ID and the owning user's total.</summary>
    public void Increment(string? userId, string eventId)
    {
        _counts.AddOrUpdate(eventId, 1, (_, existing) => existing + 1);
        if (!string.IsNullOrEmpty(userId))
            _userTotals.AddOrUpdate(userId, 1, (_, existing) => existing + 1);
    }

    /// <summary>Returns the current write count for the given event ID (0 if never written).</summary>
    public int Get(string eventId) => _counts.TryGetValue(eventId, out var count) ? count : 0;

    /// <summary>
    /// Returns the total outbound writes recorded for a single user since the last reset. The user
    /// is the scenario's isolated test user, so this is safe under parallel execution.
    /// </summary>
    public int TotalForUser(string userId) => _userTotals.TryGetValue(userId, out var count) ? count : 0;

    /// <summary>
    /// Resets the per-user total for a single user. Per-event counts are keyed by unique
    /// (Guid-based) event IDs and never collide across scenarios, so they need no reset; a GLOBAL
    /// reset under the parallel E2E runner would wipe a concurrent scenario's counts mid-flight
    /// (the FHQ-31 ClearAll race), so reset is scoped to the scenario's own isolated user.
    /// </summary>
    public void Reset(string userId) => _userTotals.TryRemove(userId, out _);

    /// <summary>Resets all counters. Not used by the parallel scenario hooks — see <see cref="Reset(string)"/>.</summary>
    public void ResetAll()
    {
        _counts.Clear();
        _userTotals.Clear();
    }
}
