namespace FamilyHQ.Core.DTOs;

using FamilyHQ.Core.Enums;

public record WeatherResponse(
    WeatherCondition CurrentCondition,
    double CurrentTemperatureCelsius,
    double CurrentWindSpeedKmh,
    List<WeatherHourlyItem> HourlyForecasts,
    List<WeatherDailyItem> DailyForecasts);

public record WeatherHourlyItem(
    DateTimeOffset Time,
    WeatherCondition Condition,
    double TemperatureCelsius,
    double WindSpeedKmh);

public record WeatherDailyItem(
    DateOnly Date,
    WeatherCondition Condition,
    double HighCelsius,
    double LowCelsius,
    double WindSpeedMaxKmh);
