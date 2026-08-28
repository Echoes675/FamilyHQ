using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DayThemeController : ControllerBase
{
    private readonly IDayThemeService _dayThemeService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DayThemeController> _logger;

    public DayThemeController(
        IDayThemeService dayThemeService,
        ICurrentUserService currentUser,
        ILogger<DayThemeController> logger)
    {
        _dayThemeService = dayThemeService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// FHQ-177: authenticated, and no longer <c>[AllowAnonymous]</c>. The theme is per-kiosk, so the
    /// caller's identity is what selects the row. It also stops the endpoint handing the timezone and
    /// four solar times to anyone on the internet — together those give away roughly where the family
    /// lives, which FHQ-166 already ruled out saying in logs.
    /// </summary>
    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var dto = await _dayThemeService.GetTodayAsync(_currentUser.UserId!, ct);

        // No saved location for this kiosk: a normal state, not a fault. The client falls back to its
        // default theme rather than showing an error on a wall display.
        if (dto is null) return NoContent();

        return Ok(dto);
    }
}
