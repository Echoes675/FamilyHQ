namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.DTOs;

public interface IWeatherUiService : IAsyncDisposable
{
    CurrentWeatherDto? CurrentWeather { get; }
    List<DailyForecastItemDto> DailyForecast { get; }
    List<HourlyForecastItemDto> HourlyForecast { get; }
    WeatherSettingDto? Settings { get; }
    bool IsEnabled { get; }

    event Action? OnWeatherChanged;

    Task InitialiseAsync();
    Task RefreshAsync();
    Task LoadHourlyAsync(DateOnly date);
    Task LoadDailyAsync(int days);
    Task<WeatherSettingDto> LoadSettingsAsync();
    Task<WeatherSettingDto> SaveSettingsAsync(WeatherSettingDto dto);
}
