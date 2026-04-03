namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherSettingDto(
    bool Enabled,
    int PollIntervalMinutes,
    TemperatureUnit TemperatureUnit,
    double WindThresholdKmh,
    string? ApiKey);
