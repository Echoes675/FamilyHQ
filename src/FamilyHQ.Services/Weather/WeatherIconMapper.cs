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
        _ => "clear"
    };
}
