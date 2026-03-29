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
    private readonly ILogger<DayThemeController> _logger;

    public DayThemeController(
        IDayThemeService dayThemeService,
        ILogger<DayThemeController> logger)
    {
        _dayThemeService = dayThemeService;
        _logger = logger;
    }

    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var dto = await _dayThemeService.GetTodayAsync(ct);
        return Ok(dto);
    }
}
