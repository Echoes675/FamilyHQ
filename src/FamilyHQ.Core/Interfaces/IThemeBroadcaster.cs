namespace FamilyHQ.Core.Interfaces;

public interface IThemeBroadcaster
{
    /// <summary>
    /// Signals connected kiosks that a period boundary has passed. Carries **no period**: since
    /// FHQ-177 the period is per-kiosk, so pushing one value to every client would be actively wrong
    /// for any kiosk in a different location. Each kiosk responds by re-reading its own
    /// <c>GET /api/daytheme/today</c>, which is authenticated and therefore already kiosk-scoped.
    /// </summary>
    Task BroadcastThemeChangedAsync(CancellationToken cancellationToken = default);
}
