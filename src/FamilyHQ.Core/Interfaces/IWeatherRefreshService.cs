namespace FamilyHQ.Core.Interfaces;

public interface IWeatherRefreshService
{
    Task RefreshAsync(CancellationToken ct = default);
}
