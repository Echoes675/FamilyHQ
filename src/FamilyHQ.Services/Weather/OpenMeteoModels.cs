namespace FamilyHQ.Services.Weather;

using System.Text.Json.Serialization;

public record OpenMeteoApiResponse(
    [property: JsonPropertyName("current")] OpenMeteoCurrentData? Current,
    [property: JsonPropertyName("hourly")] OpenMeteoHourlyData? Hourly,
    [property: JsonPropertyName("daily")] OpenMeteoDailyData? Daily);

public record OpenMeteoCurrentData(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("temperature_2m")] double Temperature,
    [property: JsonPropertyName("weather_code")] int WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] double WindSpeed);

public record OpenMeteoHourlyData(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("temperature_2m")] List<double?> Temperature,
    [property: JsonPropertyName("weather_code")] List<int?> WeatherCode,
    [property: JsonPropertyName("wind_speed_10m")] List<double?> WindSpeed);

public record OpenMeteoDailyData(
    [property: JsonPropertyName("time")] List<string> Time,
    [property: JsonPropertyName("weather_code")] List<int?> WeatherCode,
    [property: JsonPropertyName("temperature_2m_max")] List<double?> TemperatureMax,
    [property: JsonPropertyName("temperature_2m_min")] List<double?> TemperatureMin,
    [property: JsonPropertyName("wind_speed_10m_max")] List<double?> WindSpeedMax);
