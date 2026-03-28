namespace FamilyHQ.Core.Models;

public record WeatherState(
    WeatherCondition Condition,
    double? TemperatureCelsius,
    DateTimeOffset ObservedAt
);
