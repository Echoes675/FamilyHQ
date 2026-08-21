namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherDailyItem(
    DateOnly Date,
    WeatherCondition Condition,
    double HighCelsius,
    double LowCelsius,
    double WindSpeedMaxKmh);
