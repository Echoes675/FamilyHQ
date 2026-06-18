namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.DTOs;

public interface IWeatherProvider
{
    Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude,
        string? ianaTimeZone, CancellationToken ct = default);
}
