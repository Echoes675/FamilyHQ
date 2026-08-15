namespace FamilyHQ.Core.Enums;

public enum WeatherCondition
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    LightRain,
    HeavyRain,
    Thunder,
    Snow,
    Sleet,

    // Appended deliberately: WeatherDataPoint.Condition is persisted as an integer, so
    // inserting a member anywhere above would re-label every stored weather row.
    Unknown
}
