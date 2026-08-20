namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

/// <summary>
/// Read side of the weather feature — and the single place the FHQ-159 retention windows are
/// applied. They live here, on the read, rather than in the repository or a background sweep,
/// because: this is the only funnel every family-facing weather read passes through, so one check
/// covers the kiosk, the API and any future caller; the windows are configuration, and
/// <see cref="WeatherOptions"/> belongs to the service layer, not to a Data-layer repository; and
/// filtering rather than deleting means a section is hidden the instant it ages out (a sweep hides
/// it only when the sweep next runs) while the stored rows stay available to be overwritten by the
/// next successful poll for that section.
/// </summary>
public class WeatherService(
    IWeatherDataPointRepository weatherDataPointRepository,
    IWeatherSettingRepository weatherSettingRepository,
    ILocationSettingRepository locationSettingRepository,
    ICurrentUserService currentUserService,
    ITimeZoneLookup timeZoneLookup,
    IOptions<WeatherOptions> weatherOptions,
    TimeProvider timeProvider,
    ILogger<WeatherService> logger) : IWeatherService
{
    private readonly WeatherOptions _options = weatherOptions.Value;

    public async Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var location = await GetLocationSettingAsync(ct);
        if (location is null)
            return null;

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var dataPoint = await weatherDataPointRepository.GetCurrentAsync(location.Id, ct);
        if (dataPoint is null)
            return null;

        if (IsStale(dataPoint.RetrievedAt, _options.CurrentStaleAfterMinutes))
        {
            LogSectionHidden(WeatherDataType.Current, location.Id, dataPoint.RetrievedAt,
                _options.CurrentStaleAfterMinutes);
            return null;
        }

        return MapToCurrentDto(dataPoint, setting.TemperatureUnit);
    }

    public async Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct = default)
    {
        var location = await GetLocationSettingAsync(ct);
        if (location is null)
            return [];

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var ianaTimeZone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
        var dataPoints = await weatherDataPointRepository.GetHourlyAsync(location.Id, date, ianaTimeZone, ct);

        return FreshOnly(dataPoints, WeatherDataType.Hourly, location.Id)
            .Select(dp => MapToHourlyDto(dp, setting.TemperatureUnit))
            .ToList();
    }

    public async Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct = default)
    {
        var location = await GetLocationSettingAsync(ct);
        if (location is null)
            return [];

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var ianaTimeZone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
        var zone = ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null;
        var dataPoints = await weatherDataPointRepository.GetDailyAsync(location.Id, days, ianaTimeZone, ct);

        return FreshOnly(dataPoints, WeatherDataType.Daily, location.Id)
            .Select(dp => MapToDailyDto(dp, setting.TemperatureUnit, zone))
            .ToList();
    }

    public async Task<WeatherSettingDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        return MapToDto(setting, maskApiKey: true);
    }

    public async Task<WeatherSettingDto> UpdateSettingsAsync(WeatherSettingDto dto, CancellationToken ct = default)
    {
        var existing = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);

        existing.Enabled = dto.Enabled;
        existing.PollIntervalMinutes = dto.PollIntervalMinutes;
        existing.TemperatureUnit = dto.TemperatureUnit;
        existing.WindThresholdKmh = dto.WindThresholdKmh;

        // Only update the API key if a non-null value was provided (null means "leave unchanged")
        if (dto.ApiKey is not null)
            existing.ApiKey = dto.ApiKey;

        var updated = await weatherSettingRepository.UpsertAsync(currentUserService.UserId!, existing, ct);
        return MapToDto(updated, maskApiKey: true);
    }

    private async Task<LocationSetting?> GetLocationSettingAsync(CancellationToken ct)
        => await locationSettingRepository.GetAsync(currentUserService.UserId!, ct);

    /// <summary>
    /// Drops forecast rows whose refresh is past <see cref="WeatherOptions.ForecastStaleAfterMinutes"/>.
    /// Every row a refresh writes carries that refresh's <c>RetrievedAt</c>, so this is a per-section
    /// decision even though it is expressed per row.
    /// </summary>
    private List<WeatherDataPoint> FreshOnly(
        List<WeatherDataPoint> dataPoints, WeatherDataType section, int locationSettingId)
    {
        var fresh = dataPoints
            .Where(dp => !IsStale(dp.RetrievedAt, _options.ForecastStaleAfterMinutes))
            .ToList();

        if (fresh.Count < dataPoints.Count)
        {
            LogSectionHidden(section, locationSettingId,
                dataPoints.Max(dp => dp.RetrievedAt), _options.ForecastStaleAfterMinutes);
        }

        return fresh;
    }

    private bool IsStale(DateTimeOffset retrievedAt, int staleAfterMinutes)
        => timeProvider.GetUtcNow() - retrievedAt > TimeSpan.FromMinutes(staleAfterMinutes);

    // Debug, not Warning: the poller already reports the upstream failure that caused the gap, and
    // this fires on every dashboard read for as long as the gap lasts. The location id is an opaque
    // key, never a place name.
    private void LogSectionHidden(
        WeatherDataType section, int locationSettingId, DateTimeOffset retrievedAt, int staleAfterMinutes)
        => logger.LogDebug(
            "Hiding {WeatherSection} weather for location {LocationSettingId}: last retrieved at {RetrievedAt}, past the {StaleAfterMinutes}-minute retention window.",
            section, locationSettingId, retrievedAt, staleAfterMinutes);

    private static CurrentWeatherDto MapToCurrentDto(WeatherDataPoint dp, TemperatureUnit unit) =>
        new(
            Condition: dp.Condition,
            Temperature: TemperatureConverter.Convert(dp.TemperatureCelsius, unit),
            IsWindy: dp.IsWindy,
            WindSpeedKmh: dp.WindSpeedKmh,
            IconName: WeatherIconMapper.ToIconName(dp.Condition));

    private static HourlyForecastItemDto MapToHourlyDto(WeatherDataPoint dp, TemperatureUnit unit) =>
        new(
            Hour: dp.Timestamp,
            Condition: dp.Condition,
            Temperature: TemperatureConverter.Convert(dp.TemperatureCelsius, unit),
            IsWindy: dp.IsWindy,
            IconName: WeatherIconMapper.ToIconName(dp.Condition));

    private static DailyForecastItemDto MapToDailyDto(WeatherDataPoint dp, TemperatureUnit unit, DateTimeZone? zone)
    {
        var localDate = zone is not null
            ? Instant.FromDateTimeOffset(dp.Timestamp).InZone(zone).Date
            : LocalDate.FromDateTime(dp.Timestamp.UtcDateTime);
        return new(
            Date: new DateOnly(localDate.Year, localDate.Month, localDate.Day),
            Condition: dp.Condition,
            High: TemperatureConverter.Convert(dp.HighCelsius ?? dp.TemperatureCelsius, unit),
            Low: TemperatureConverter.Convert(dp.LowCelsius ?? dp.TemperatureCelsius, unit),
            IsWindy: dp.IsWindy,
            IconName: WeatherIconMapper.ToIconName(dp.Condition));
    }

    private static WeatherSettingDto MapToDto(WeatherSetting setting, bool maskApiKey) =>
        new(
            Enabled: setting.Enabled,
            PollIntervalMinutes: setting.PollIntervalMinutes,
            TemperatureUnit: setting.TemperatureUnit,
            WindThresholdKmh: setting.WindThresholdKmh,
            ApiKey: maskApiKey ? MaskApiKey(setting.ApiKey) : setting.ApiKey);

    private static string? MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        if (apiKey.Length <= 4)
            return new string('*', apiKey.Length);

        return string.Concat(new string('*', apiKey.Length - 4), apiKey.AsSpan(apiKey.Length - 4));
    }
}
