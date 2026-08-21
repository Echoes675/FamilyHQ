namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherHourlyItem(
    DateTimeOffset Time,
    WeatherCondition Condition,
    double TemperatureCelsius,
    double WindSpeedKmh);
