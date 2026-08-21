namespace FamilyHQ.Core.DTOs;

public record WeatherResponse(
    WeatherCurrentItem? Current,
    List<WeatherHourlyItem> HourlyForecasts,
    List<WeatherDailyItem> DailyForecasts);
