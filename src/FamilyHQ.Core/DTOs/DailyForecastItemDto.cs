namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record DailyForecastItemDto(
    DateOnly Date,
    WeatherCondition Condition,
    double High,
    double Low,
    bool IsWindy,
    string IconName);
