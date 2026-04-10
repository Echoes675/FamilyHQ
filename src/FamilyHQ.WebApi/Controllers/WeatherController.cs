namespace FamilyHQ.WebApi.Controllers;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class WeatherController(
    IWeatherService weatherService,
    IWeatherRefreshService weatherRefreshService,
    ICurrentUserService currentUser) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
            return Unauthorized();

        var result = await weatherRefreshService.RefreshAsync(userId, ct);

        // A silent skip previously returned 200 with no data written, which
        // caused intermittent E2E flakes where /api/weather/current then
        // returned 204.  Surface skips explicitly so clients fail fast.
        // The userId is included in the 409 body so that a failing E2E run can
        // compare the server-observed user against the JWT sub in localStorage
        // and diagnose whether the refresh call resolved the same identity as
        // the preceding save-location call.  See the diagnostic plan in
        // fix/weather-refresh-race for the three-row decision matrix.
        if (result.Outcome == WeatherRefreshOutcome.SkippedWeatherDisabled)
            return Conflict(new { message = "Weather refresh skipped: weather is disabled for this user.", userId });

        if (result.Outcome == WeatherRefreshOutcome.SkippedNoLocation)
            return Conflict(new { message = "Weather refresh skipped: no saved location for this user.", userId });

        // Verify the refresh actually produced data that is visible to a
        // subsequent read.  The intermittent failure we are guarding against
        // is RefreshAsync reporting success while /current sees no current
        // data point.  By checking visibility here we turn an obscure
        // downstream 204 into a single clear 503 on the refresh call itself.
        var current = await weatherService.GetCurrentAsync(ct);
        if (current is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Weather refresh reported success but no current data is visible.",
                locationSettingId = result.LocationSettingId,
                dataPointsWritten = result.DataPointsWritten
            });
        }

        return Ok(new { message = "Weather refreshed", dataPointsWritten = result.DataPointsWritten });
    }

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
