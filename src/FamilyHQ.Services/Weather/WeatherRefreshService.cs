namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.Extensions.Logging;

public class WeatherRefreshService(
    IWeatherSettingRepository weatherSettingRepo,
    ILocationSettingRepository locationRepo,
    IWeatherProvider weatherProvider,
    IWeatherDataPointRepository weatherDataPointRepo,
    IWeatherBroadcaster weatherBroadcaster,
    ILogger<WeatherRefreshService> logger) : IWeatherRefreshService
{
    public Task RefreshAsync(CancellationToken ct = default)
        => RefreshCoreAsync(null, ct);

    public Task RefreshAsync(string userId, CancellationToken ct = default)
        => RefreshCoreAsync(userId, ct);

    private async Task RefreshCoreAsync(string? userId, CancellationToken ct)
    {
        var weatherSetting = await weatherSettingRepo.GetOrCreateAsync(ct);

        if (!weatherSetting.Enabled)
        {
            logger.LogInformation("Weather is disabled. Skipping refresh.");
            return;
        }

        var location = userId is not null
            ? await locationRepo.GetAsync(userId, ct)
            : await locationRepo.GetAsync(ct);

        if (location is null)
        {
            logger.LogInformation("No location configured. Skipping weather refresh.");
            return;
        }

        var weatherResponse = await weatherProvider.GetWeatherAsync(
            location.Latitude, location.Longitude, ct);

        var now = DateTimeOffset.UtcNow;
        var windThreshold = weatherSetting.WindThresholdKmh;

        var dataPoints = BuildDataPoints(location.Id, weatherResponse, now, windThreshold);

        await weatherDataPointRepo.ReplaceAllAsync(location.Id, dataPoints, ct);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(ct);

        logger.LogInformation(
            "Weather data updated for location {PlaceName} ({Lat}, {Lon})",
            location.PlaceName, location.Latitude, location.Longitude);
    }

    internal static List<WeatherDataPoint> BuildDataPoints(
        int locationSettingId,
        WeatherResponse response,
        DateTimeOffset retrievedAt,
        double windThresholdKmh)
    {
        var dataPoints = new List<WeatherDataPoint>();

        // Current
        dataPoints.Add(new WeatherDataPoint
        {
            LocationSettingId = locationSettingId,
            Timestamp = retrievedAt,
            Condition = response.CurrentCondition,
            TemperatureCelsius = response.CurrentTemperatureCelsius,
            WindSpeedKmh = response.CurrentWindSpeedKmh,
            IsWindy = response.CurrentWindSpeedKmh >= windThresholdKmh,
            DataType = WeatherDataType.Current,
            RetrievedAt = retrievedAt
        });

        // Hourly
        foreach (var hourly in response.HourlyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = hourly.Time,
                Condition = hourly.Condition,
                TemperatureCelsius = hourly.TemperatureCelsius,
                WindSpeedKmh = hourly.WindSpeedKmh,
                IsWindy = hourly.WindSpeedKmh >= windThresholdKmh,
                DataType = WeatherDataType.Hourly,
                RetrievedAt = retrievedAt
            });
        }

        // Daily
        foreach (var daily in response.DailyForecasts)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = new DateTimeOffset(daily.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                Condition = daily.Condition,
                TemperatureCelsius = daily.HighCelsius,
                HighCelsius = daily.HighCelsius,
                LowCelsius = daily.LowCelsius,
                WindSpeedKmh = daily.WindSpeedMaxKmh,
                IsWindy = daily.WindSpeedMaxKmh >= windThresholdKmh,
                DataType = WeatherDataType.Daily,
                RetrievedAt = retrievedAt
            });
        }

        return dataPoints;
    }
}
