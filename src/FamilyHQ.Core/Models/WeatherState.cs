namespace FamilyHQ.Core.Models;

public enum WeatherCondition
{
    Clear,
    Cloudy,
    LightRain,
    HeavyRain,
    Thunder,
    Snow,
    WindMist
}

public record WeatherState(
    WeatherCondition Condition,
    double? TemperatureCelsius,
    DateTimeOffset ObservedAt
);
