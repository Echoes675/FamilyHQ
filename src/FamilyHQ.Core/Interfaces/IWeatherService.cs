namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.DTOs;

public interface IWeatherService
{
    Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default);
    Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct = default);
    Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct = default);
    Task<WeatherSettingDto> GetSettingsAsync(CancellationToken ct = default);
    Task<WeatherSettingDto> UpdateSettingsAsync(WeatherSettingDto dto, CancellationToken ct = default);
}
