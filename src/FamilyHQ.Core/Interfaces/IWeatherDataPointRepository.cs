namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWeatherDataPointRepository
{
    Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date,
        string? ianaTimeZone, CancellationToken ct = default);
    Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days,
        string? ianaTimeZone, CancellationToken ct = default);
    /// <summary>
    /// Replaces the stored rows for every section (<see cref="Enums.WeatherDataType"/>) present in
    /// <paramref name="dataPoints"/>, atomically, and leaves every other section untouched. FHQ-159:
    /// a section the refresh did not carry — an empty or absent Open-Meteo block — must keep the
    /// data it already had rather than being blanked as a side effect of rewriting its neighbours.
    /// </summary>
    Task ReplaceSectionsAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default);
}
