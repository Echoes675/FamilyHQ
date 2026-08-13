namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Read-only view of the SignalR connection health for UI consumers
/// (e.g. the stale-data indicator). Kept separate from
/// <see cref="ISignalRConnectionCoordinator"/> so components cannot feed
/// lifecycle events in.
/// </summary>
public interface ISignalRConnectionMonitor
{
    /// <summary>
    /// True while the hub connection is down (initial start failed, reconnect in
    /// progress, or the connection closed and background restarts are running).
    /// False before the first start attempt and while connected.
    /// </summary>
    bool IsConnectionDown { get; }

    /// <summary>Raised whenever <see cref="IsConnectionDown"/> changes.</summary>
    event Action? ConnectionStateChanged;
}
