using FamilyHQ.Services.Weather;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/weather")]
[Authorize]
public class WeatherController : ControllerBase
{
    private readonly WeatherBackgroundService _weatherService;

    public WeatherController(WeatherBackgroundService weatherService)
    {
        _weatherService = weatherService;
    }

    [HttpGet("current")]
    public IActionResult GetCurrent()
    {
        var state = _weatherService.CurrentState;
        if (state is null)
            return Ok(new { condition = "Clear", temperatureCelsius = (double?)null, observedAt = DateTimeOffset.UtcNow });
        
        return Ok(new
        {
            condition = state.Condition.ToString(),
            temperatureCelsius = state.TemperatureCelsius,
            observedAt = state.ObservedAt
        });
    }
}
