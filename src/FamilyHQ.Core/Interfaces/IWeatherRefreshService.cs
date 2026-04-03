namespace FamilyHQ.Core.Interfaces;

public interface IWeatherRefreshService
{
    /// <summary>Refreshes weather using the specified user's saved location and settings.</summary>
    Task RefreshAsync(string userId, CancellationToken ct = default);
}
