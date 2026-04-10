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
    public async Task<WeatherRefreshResult> RefreshAsync(string userId, CancellationToken ct = default)
    {
        logger.LogInformation("Weather refresh requested for user {UserId}.", userId);

        var weatherSetting = await weatherSettingRepo.GetOrCreateAsync(userId, ct);

        if (!weatherSetting.Enabled)
        {
            logger.LogInformation("Weather is disabled for user {UserId}. Skipping refresh.", userId);
            return new WeatherRefreshResult(WeatherRefreshOutcome.SkippedWeatherDisabled, LocationSettingId: null, DataPointsWritten: 0);
        }

        var location = await locationRepo.GetAsync(userId, ct);

        if (location is null)
        {
            logger.LogWarning("No location configured for user {UserId}. Skipping weather refresh.", userId);
            return new WeatherRefreshResult(WeatherRefreshOutcome.SkippedNoLocation, LocationSettingId: null, DataPointsWritten: 0);
        }

        var weatherResponse = await weatherProvider.GetWeatherAsync(
            location.Latitude, location.Longitude, ct);

        var now = DateTimeOffset.UtcNow;
        var windThreshold = weatherSetting.WindThresholdKmh;

        var dataPoints = BuildDataPoints(location.Id, weatherResponse, now, windThreshold);

        await weatherDataPointRepo.ReplaceAllAsync(location.Id, dataPoints, ct);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(ct);

        logger.LogInformation(
            "Weather data updated for user {UserId}, location {LocationId} ({PlaceName} @ {Lat}, {Lon}). Wrote {DataPointsWritten} data points.",
            userId, location.Id, location.PlaceName, location.Latitude, location.Longitude, dataPoints.Count);

        return new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, location.Id, dataPoints.Count);
    }

    internal static List<WeatherDataPoint> BuildDataPoints(
        int locationSettingId,
        WeatherResponse response,
        DateTimeOffset retrievedAt,
        double windThresholdKmh)
    {
        var dataPoints = new List<WeatherDataPoint>();

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
