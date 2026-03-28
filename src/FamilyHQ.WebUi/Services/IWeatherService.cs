namespace FamilyHQ.WebUi.Services;

public interface IWeatherService
{
    WeatherConditionDto CurrentCondition { get; }
    event EventHandler<WeatherConditionDto>? WeatherChanged;
    Task StartAsync();
}