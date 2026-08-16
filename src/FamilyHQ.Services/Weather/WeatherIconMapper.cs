namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Enums;

public static class WeatherIconMapper
{
    public static string ToIconName(WeatherCondition condition) => condition switch
    {
        WeatherCondition.Clear => "clear",
        WeatherCondition.PartlyCloudy => "partly-cloudy",
        WeatherCondition.Cloudy => "cloudy",
        WeatherCondition.Fog => "fog",
        WeatherCondition.Drizzle => "drizzle",
        WeatherCondition.LightRain => "light-rain",
        WeatherCondition.HeavyRain => "heavy-rain",
        WeatherCondition.Thunder => "thunder",
        WeatherCondition.Snow => "snow",
        WeatherCondition.Sleet => "sleet",
        WeatherCondition.Unknown => "unknown",
        // FHQ-115: never fall back to "clear" — an unrecognised condition must not draw a
        // sun on the dashboard.
        _ => "unknown"
    };
}
