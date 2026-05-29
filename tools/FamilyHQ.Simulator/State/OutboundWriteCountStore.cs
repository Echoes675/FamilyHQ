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

    /// <summary>Increments the write count for the given event ID.</summary>
    public void Increment(string eventId)
    {
        _counts.AddOrUpdate(eventId, 1, (_, existing) => existing + 1);
    }

    /// <summary>Returns the current write count for the given event ID (0 if never written).</summary>
    public int Get(string eventId) => _counts.TryGetValue(eventId, out var count) ? count : 0;

    /// <summary>
    /// Returns the total number of outbound writes recorded across every event ID since the last
    /// reset. Used by recurring-events echo-guard assertions where the written event's ID is
    /// generated server-side (native series creation) and so is not known to the test up front.
    /// </summary>
    public int Total() => _counts.Values.Sum();

    /// <summary>Resets all counters — call between scenarios.</summary>
    public void Reset() => _counts.Clear();
}
