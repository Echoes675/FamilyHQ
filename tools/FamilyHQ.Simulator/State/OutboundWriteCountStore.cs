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

    /// <summary>Resets all counters — call between scenarios.</summary>
    public void Reset() => _counts.Clear();
}
