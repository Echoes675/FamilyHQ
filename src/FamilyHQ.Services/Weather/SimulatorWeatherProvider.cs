using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using System.Net.Http.Json;

namespace FamilyHQ.Services.Weather;

/// <summary>
/// Weather provider that reads from the FamilyHQ Simulator's mock weather endpoint.
/// Used in development and E2E test environments.
/// </summary>
public sealed class SimulatorWeatherProvider : IWeatherProvider
{
    private readonly HttpClient _httpClient;

    public SimulatorWeatherProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<SimulatorWeatherResponse>(
                "/api/simulator/weather", cancellationToken);
            
            if (response is null) return null;
            
            if (!Enum.TryParse<WeatherCondition>(response.Condition, ignoreCase: true, out var condition))
                condition = WeatherCondition.Clear;
            
            return new WeatherState(condition, response.TemperatureCelsius, DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private record SimulatorWeatherResponse(string Condition, double? TemperatureCelsius);
}
