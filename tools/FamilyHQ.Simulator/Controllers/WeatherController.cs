using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("api/simulator/weather")]
public class WeatherController : ControllerBase
{
    private static WeatherState? _currentWeather;

    [HttpGet]
    public IActionResult GetCurrent()
    {
        if (_currentWeather is null)
        {
            return Ok(new { condition = "Clear", temperatureCelsius = (double?)null, observedAt = DateTimeOffset.UtcNow });
        }
        
        return Ok(new
        {
            condition = _currentWeather.Condition.ToString(),
            temperatureCelsius = _currentWeather.TemperatureCelsius,
            observedAt = _currentWeather.ObservedAt
        });
    }

    [HttpPost]
    public IActionResult SetCurrent([FromBody] SetWeatherRequest request)
    {
        if (!Enum.TryParse<WeatherCondition>(request.Condition, ignoreCase: true, out var condition))
        {
            return BadRequest(new { error = "Invalid weather condition" });
        }

        _currentWeather = new WeatherState(
            Condition: condition,
            TemperatureCelsius: request.TemperatureCelsius,
            ObservedAt: DateTimeOffset.UtcNow
        );

        return Ok(new
        {
            condition = _currentWeather.Condition.ToString(),
            temperatureCelsius = _currentWeather.TemperatureCelsius,
            observedAt = _currentWeather.ObservedAt
        });
    }

    public record SetWeatherRequest(string Condition, double? TemperatureCelsius);
}
