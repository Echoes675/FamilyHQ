namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Weather;
using Microsoft.Extensions.Logging;
using NodaTime;

public class WeatherRefreshService(
    IWeatherSettingRepository weatherSettingRepo,
    ILocationSettingRepository locationRepo,
    IWeatherProvider weatherProvider,
    IWeatherDataPointRepository weatherDataPointRepo,
    IWeatherBroadcaster weatherBroadcaster,
    ITimeZoneLookup timeZoneLookup,
    TimeProvider timeProvider,
    ILogger<WeatherRefreshService> logger) : IWeatherRefreshService
{
    /// <summary>Every section a healthy Open-Meteo response carries.</summary>
    private static readonly WeatherDataType[] AllSections =
        [WeatherDataType.Current, WeatherDataType.Hourly, WeatherDataType.Daily];

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
            logger.LogDebug("No location configured for user {UserId}. Skipping weather refresh.", userId);
            return new WeatherRefreshResult(WeatherRefreshOutcome.SkippedNoLocation, LocationSettingId: null, DataPointsWritten: 0);
        }

        var ianaTimeZone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);

        if (ianaTimeZone is not null &&
            DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone) is null)
        {
            logger.LogWarning(
                "ITimeZoneLookup returned an unknown IANA zone '{Zone}' for location {LocationId}; timestamps will be treated as UTC.",
                ianaTimeZone, location.Id);
        }

        var weatherResponse = await weatherProvider.GetWeatherAsync(
            location.Latitude, location.Longitude, ianaTimeZone, ct);

        // Every row this refresh writes carries the same RetrievedAt, and WeatherService measures
        // the retention windows against it — so both ends of the window must read the same clock
        // (FHQ-159). DateTimeOffset.UtcNow here made half of it untestable.
        var now = timeProvider.GetUtcNow();
        var windThreshold = weatherSetting.WindThresholdKmh;

        var dataPoints = BuildDataPoints(location.Id, weatherResponse, now, windThreshold, ianaTimeZone);

        await weatherDataPointRepo.ReplaceSectionsAsync(location.Id, dataPoints, ct);

        await weatherBroadcaster.BroadcastWeatherUpdatedAsync(ct);

        // FHQ-166: no place name and no coordinates. Together they are the family's home address to
        // within a few metres, and this line is emitted at Information on every successful refresh —
        // once per user every WeatherOptions.PollIntervalMinutes, in every environment. LocationId
        // is the correlation key an investigation actually uses and it is already here.
        logger.LogInformation(
            "Weather data updated for user {UserId}, location {LocationId}. Wrote {DataPointsWritten} data points.",
            userId, location.Id, dataPoints.Count);

        LogDegradedResponse(userId, location.Id, dataPoints);

        return new WeatherRefreshResult(WeatherRefreshOutcome.Succeeded, location.Id, dataPoints.Count);
    }

    internal static List<WeatherDataPoint> BuildDataPoints(
        int locationSettingId,
        WeatherResponse response,
        DateTimeOffset retrievedAt,
        double windThresholdKmh,
        string? ianaTimeZone = null)
    {
        var dataPoints = new List<WeatherDataPoint>();

        // FHQ-159: a response with no current block writes NO Current row. It used to write one
        // carrying a fabricated 0 °C / 0 km/h, so the kiosk could show "Unknown, 0°, 0 km/h" as a
        // reading about now. Writing nothing leaves the previous reading in place, and
        // WeatherService stops showing that once it passes WeatherOptions.CurrentStaleAfterMinutes.
        if (response.Current is not null)
        {
            dataPoints.Add(new WeatherDataPoint
            {
                LocationSettingId = locationSettingId,
                Timestamp = retrievedAt,
                Condition = response.Current.Condition,
                TemperatureCelsius = response.Current.TemperatureCelsius,
                WindSpeedKmh = response.Current.WindSpeedKmh,
                IsWindy = response.Current.WindSpeedKmh >= windThresholdKmh,
                DataType = WeatherDataType.Current,
                RetrievedAt = retrievedAt
            });
        }

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
                Timestamp = BuildDailyTimestamp(daily.Date, ianaTimeZone),
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

    /// <summary>
    /// FHQ-159: the production signal for the failure this ticket exists for. A degraded Open-Meteo
    /// response is a well-formed 200, so <see cref="WeatherPollerService"/> records it as a success
    /// and the empty-section detail is Debug (off in production) — without this, a section could
    /// quietly age out of its retention window and vanish from the kiosk with nothing in Seq above
    /// Debug to explain it. Information, once per refresh: at most every
    /// <see cref="Options.WeatherOptions.PollIntervalMinutes"/> per user, not once per read.
    /// </summary>
    private void LogDegradedResponse(string userId, int locationSettingId, List<WeatherDataPoint> dataPoints)
    {
        var carried = WeatherRetention.SectionsReplacedBy(dataPoints);
        if (carried.Count == AllSections.Length)
            return;

        logger.LogInformation(
            "Weather refresh for user {UserId}, location {LocationId} succeeded but carried no {MissingSections} data. Those sections keep their stored values until their retention window expires.",
            userId, locationSettingId, AllSections.Where(s => !carried.Contains(s)).ToArray());
    }

    private static DateTimeOffset BuildDailyTimestamp(DateOnly date, string? ianaTimeZone)
    {
        var zone = ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null;
        if (zone is not null)
        {
            var localDate = new LocalDate(date.Year, date.Month, date.Day);
            return zone.AtStartOfDay(localDate).ToDateTimeOffset();
        }
        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
