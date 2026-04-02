namespace FamilyHQ.WebApi.Controllers;

using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class WeatherController(IWeatherService weatherService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var result = await weatherService.GetCurrentAsync(ct);
        if (result is null)
            return NoContent();
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourly([FromQuery] string date, CancellationToken ct)
    {
        if (!DateOnly.TryParse(date, out var parsedDate))
            return BadRequest("Invalid date format. Use yyyy-MM-dd.");
        var result = await weatherService.GetHourlyAsync(parsedDate, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast([FromQuery] int days = 5, CancellationToken ct = default)
    {
        if (days is < 1 or > 16)
            return BadRequest("Days must be between 1 and 16.");
        var result = await weatherService.GetDailyForecastAsync(days, ct);
        return Ok(result);
    }
}
