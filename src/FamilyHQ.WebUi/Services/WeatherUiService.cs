namespace FamilyHQ.WebUi.Services;

using System.Net.Http.Json;
using FamilyHQ.Core.DTOs;

public class WeatherUiService : IWeatherUiService
{
    private readonly HttpClient _httpClient;
    private readonly SignalRService _signalRService;
    private readonly Action _weatherUpdatedHandler;

    public CurrentWeatherDto? CurrentWeather { get; private set; }
    public List<DailyForecastItemDto> DailyForecast { get; private set; } = [];
    public List<HourlyForecastItemDto> HourlyForecast { get; private set; } = [];
    public WeatherSettingDto? Settings { get; private set; }
    public bool IsEnabled => Settings?.Enabled ?? true;

    public event Action? OnWeatherChanged;

    public WeatherUiService(HttpClient httpClient, SignalRService signalRService)
    {
        _httpClient = httpClient;
        _signalRService = signalRService;

        _weatherUpdatedHandler = () => _ = RefreshAsync();
        _signalRService.OnWeatherUpdated += _weatherUpdatedHandler;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");
            if (Settings?.Enabled != true) return;
            CurrentWeather = await _httpClient.GetFromJsonAsync<CurrentWeatherDto>("api/weather/current");
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>("api/weather/forecast?days=14") ?? [];
        }
        catch (HttpRequestException)
        {
            // Non-critical — weather may not be available yet
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");
            if (Settings?.Enabled != true)
            {
                CurrentWeather = null;
                DailyForecast = [];
                HourlyForecast = [];
                OnWeatherChanged?.Invoke();
                return;
            }
            CurrentWeather = await _httpClient.GetFromJsonAsync<CurrentWeatherDto>("api/weather/current");
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>("api/weather/forecast?days=14") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException) { }
    }

    public async Task LoadHourlyAsync(DateOnly date)
    {
        try
        {
            HourlyForecast = await _httpClient.GetFromJsonAsync<List<HourlyForecastItemDto>>(
                $"api/weather/hourly?date={date:yyyy-MM-dd}") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException) { }
    }

    public async Task LoadDailyAsync(int days)
    {
        try
        {
            DailyForecast = await _httpClient.GetFromJsonAsync<List<DailyForecastItemDto>>(
                $"api/weather/forecast?days={days}") ?? [];
            OnWeatherChanged?.Invoke();
        }
        catch (HttpRequestException) { }
    }

    public async Task<WeatherSettingDto> LoadSettingsAsync()
    {
        Settings = await _httpClient.GetFromJsonAsync<WeatherSettingDto>("api/settings/weather");
        return Settings!;
    }

    public async Task<WeatherSettingDto> SaveSettingsAsync(WeatherSettingDto dto)
    {
        var response = await _httpClient.PutAsJsonAsync("api/settings/weather", dto);
        response.EnsureSuccessStatusCode();
        Settings = await response.Content.ReadFromJsonAsync<WeatherSettingDto>();
        OnWeatherChanged?.Invoke();
        return Settings!;
    }

    public ValueTask DisposeAsync()
    {
        _signalRService.OnWeatherUpdated -= _weatherUpdatedHandler;
        return ValueTask.CompletedTask;
    }
}
