using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IWeatherProvider
{
    /// <summary>
    /// Gets the current weather state. Returns null if weather data is unavailable.
    /// </summary>
    Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken cancellationToken = default);
}
