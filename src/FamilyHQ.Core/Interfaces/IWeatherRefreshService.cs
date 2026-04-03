namespace FamilyHQ.Core.Interfaces;

public interface IWeatherRefreshService
{
    /// <summary>Refreshes weather using whatever location is stored (used by background timer).</summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>Refreshes weather using the specified user's saved location.</summary>
    Task RefreshAsync(string userId, CancellationToken ct = default);
}
