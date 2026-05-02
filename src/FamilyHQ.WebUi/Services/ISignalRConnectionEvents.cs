namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Surfaces lifecycle events from a SignalR connection that downstream services
/// (e.g. version checks) can subscribe to without depending on the concrete
/// <see cref="SignalRService"/> or pulling in the SignalR client transitively.
/// </summary>
public interface ISignalRConnectionEvents
{
    /// <summary>Raised after the underlying hub connection has been re-established.</summary>
    event Action? Reconnected;
}
