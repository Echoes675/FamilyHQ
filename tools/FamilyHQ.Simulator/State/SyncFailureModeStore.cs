using System.Collections.Concurrent;

namespace FamilyHQ.Simulator.State;

/// <summary>
/// Failure modes that the simulator can be instructed to inject into a single
/// user's sync flow. Used by E2E tests to exercise the WebApi's resilience and
/// reauth paths without having to revoke real Google credentials.
/// </summary>
public enum SyncFailureMode
{
    None,
    RefreshTokenInvalidGrant,
    CalendarApi401,
    CalendarApi403
}

/// <summary>
/// Thread-safe, per-user mapping of injected sync failure modes. Lives as a
/// singleton in the simulator process and is queried by the OAuth and Calendar
/// API controllers before they build their normal responses.
/// </summary>
public class SyncFailureModeStore
{
    private readonly ConcurrentDictionary<string, SyncFailureMode> _modes = new();

    public SyncFailureMode Get(string userId) =>
        _modes.TryGetValue(userId, out var m) ? m : SyncFailureMode.None;

    public void Set(string userId, SyncFailureMode mode) =>
        _modes[userId] = mode;

    public void Clear(string userId) =>
        _modes.TryRemove(userId, out _);

    public void ClearAll() => _modes.Clear();
}
