namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWeatherDataPointRepository
{
    Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days, CancellationToken ct = default);
    Task ReplaceAllAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default);
}
