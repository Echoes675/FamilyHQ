namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;

public class WeatherService(
    IWeatherDataPointRepository weatherDataPointRepository,
    IWeatherSettingRepository weatherSettingRepository,
    ILocationSettingRepository locationSettingRepository,
    ICurrentUserService currentUserService) : IWeatherService
{
    public async Task<CurrentWeatherDto?> GetCurrentAsync(CancellationToken ct = default)
    {
        var locationId = await GetLocationSettingIdAsync(ct);
        if (locationId is null)
            return null;

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var dataPoint = await weatherDataPointRepository.GetCurrentAsync(locationId.Value, ct);
        if (dataPoint is null)
            return null;

        return MapToCurrentDto(dataPoint, setting.TemperatureUnit);
    }

    public async Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct = default)
    {
        var locationId = await GetLocationSettingIdAsync(ct);
        if (locationId is null)
            return [];

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var dataPoints = await weatherDataPointRepository.GetHourlyAsync(locationId.Value, date, ct);

        return dataPoints
            .Select(dp => MapToHourlyDto(dp, setting.TemperatureUnit))
            .ToList();
    }

    public async Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct = default)
    {
        var locationId = await GetLocationSettingIdAsync(ct);
        if (locationId is null)
            return [];

        var setting = await weatherSettingRepository.GetOrCreateAsync(currentUserService.UserId!, ct);
        var dataPoints = await weatherDataPointRepository.GetDailyAsync(locationId.Value, days, ct);

        return dataPoints
            .Select(dp => MapToDailyDto(dp, setting.TemperatureUnit))
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

    private async Task<int?> GetLocationSettingIdAsync(CancellationToken ct)
    {
        var location = await locationSettingRepository.GetAsync(currentUserService.UserId!, ct);
        return location?.Id;
    }

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

    private static DailyForecastItemDto MapToDailyDto(WeatherDataPoint dp, TemperatureUnit unit) =>
        new(
            Date: DateOnly.FromDateTime(dp.Timestamp.Date),
            Condition: dp.Condition,
            High: TemperatureConverter.Convert(dp.HighCelsius ?? dp.TemperatureCelsius, unit),
            Low: TemperatureConverter.Convert(dp.LowCelsius ?? dp.TemperatureCelsius, unit),
            IsWindy: dp.IsWindy,
            IconName: WeatherIconMapper.ToIconName(dp.Condition));

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
