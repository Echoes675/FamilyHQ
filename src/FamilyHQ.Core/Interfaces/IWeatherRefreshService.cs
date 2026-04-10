namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.DTOs;

public interface IWeatherRefreshService
{
    /// <summary>
    /// Refreshes weather using the specified user's saved location and settings.
    /// Returns a structured result describing whether data was written or why
    /// the refresh was skipped.  Callers that explicitly request a refresh
    /// (e.g. the /api/weather/refresh endpoint) should treat a skipped outcome
    /// as a misconfiguration and surface it instead of returning success.
    /// </summary>
    Task<WeatherRefreshResult> RefreshAsync(string userId, CancellationToken ct = default);
}
