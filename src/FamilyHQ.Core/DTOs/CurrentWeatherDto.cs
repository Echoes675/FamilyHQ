namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record CurrentWeatherDto(
    WeatherCondition Condition,
    double Temperature,
    bool IsWindy,
    double WindSpeedKmh,
    string IconName);
