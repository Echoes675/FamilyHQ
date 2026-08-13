namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Connection-state machine for the SignalR hub connection (FHQ-125).
/// <see cref="SignalRService"/> feeds raw lifecycle events in; the coordinator
/// decides what to log, what the indicator state is, and when to schedule
/// background restart attempts (bounded exponential backoff). Extracted behind
/// this seam because <c>HubConnection</c> itself is not unit-mockable.
/// </summary>
public interface ISignalRConnectionCoordinator : ISignalRConnectionMonitor
{
    /// <summary>
    /// Raised when a lost or never-established connection has been restored,
    /// either by automatic reconnect or by a background restart attempt.
    /// </summary>
    event Action? ConnectionRestored;

    /// <summary>
    /// Supplies the callback that (re)starts the underlying hub connection.
    /// Must be called exactly once before any connection events are reported.
    /// </summary>
    void Initialize(Func<CancellationToken, Task> restartAsync);

    /// <summary>The initial start attempt succeeded.</summary>
    void OnStarted();

    /// <summary>The initial start attempt failed; background restarts must take over.</summary>
    void OnStartFailed(Exception exception);

    /// <summary>The established connection was lost; automatic reconnect is in progress.</summary>
    void OnReconnecting(Exception? exception);

    /// <summary>Automatic reconnect re-established the connection.</summary>
    void OnReconnected();

    /// <summary>
    /// The connection closed permanently (automatic reconnect exhausted);
    /// background restarts must take over.
    /// </summary>
    void OnClosed(Exception? exception);
}
