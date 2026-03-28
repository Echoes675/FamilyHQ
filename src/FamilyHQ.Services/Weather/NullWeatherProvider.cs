using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Services.Weather;

/// <summary>
/// A no-op weather provider used in development/test environments
/// when no real weather provider is configured.
/// Always returns Clear weather.
/// </summary>
public sealed class NullWeatherProvider : IWeatherProvider
{
    public Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default)
    {
        var state = new WeatherState(
            Condition: WeatherCondition.Clear,
            TemperatureCelsius: null,
            ObservedAt: DateTimeOffset.UtcNow
        );
        return Task.FromResult<WeatherState?>(state);
    }
}
