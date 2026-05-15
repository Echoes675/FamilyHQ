namespace FamilyHQ.Core.Interfaces;

/// <summary>
/// Pushes a notification to all connected UI clients when a user's
/// Google connection status changes (typically AuthStatus flipping
/// to NeedsReauth or back to Active). Receivers re-fetch
/// /api/calendars/connection-status to read the new state.
/// </summary>
public interface IConnectionStatusBroadcaster
{
    Task BroadcastConnectionStatusUpdatedAsync(CancellationToken cancellationToken = default);
}
