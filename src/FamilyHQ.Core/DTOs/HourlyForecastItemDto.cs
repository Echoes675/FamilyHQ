namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record HourlyForecastItemDto(
    DateTimeOffset Hour,
    WeatherCondition Condition,
    double Temperature,
    bool IsWindy,
    string IconName);
