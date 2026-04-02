using System.Net.Http.Json;
using FamilyHQ.E2E.Common.Configuration;

namespace FamilyHQ.E2E.Data.Api;

public class WebApiClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public WebApiClient()
    {
        var config = ConfigurationLoader.Load();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        _httpClient = new HttpClient(handler) { BaseAddress = new Uri(config.ApiBaseUrl) };
    }

    /// <summary>
    /// Triggers an immediate weather data refresh on the WebApi.
    /// </summary>
    public async Task TriggerWeatherRefreshAsync()
    {
        var response = await _httpClient.PostAsync("api/weather/refresh", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Ensures weather is enabled. Reads current settings and updates if disabled.
    /// </summary>
    public async Task EnsureWeatherEnabledAsync()
    {
        var settings = await _httpClient.GetFromJsonAsync<WeatherSettingResponse>("api/settings/weather");
        if (settings is null || settings.Enabled)
            return;

        var updated = new
        {
            Enabled = true,
            settings.PollIntervalMinutes,
            settings.TemperatureUnit,
            settings.WindThresholdKmh,
            ApiKey = (string?)null
        };
        var response = await _httpClient.PutAsJsonAsync("api/settings/weather", updated);
        response.EnsureSuccessStatusCode();
    }

    private record WeatherSettingResponse(
        bool Enabled, int PollIntervalMinutes, int TemperatureUnit, double WindThresholdKmh);

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
