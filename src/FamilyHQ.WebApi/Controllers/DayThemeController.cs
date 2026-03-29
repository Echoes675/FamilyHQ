using FamilyHQ.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DayThemeController(IDayThemeService dayThemeService) : ControllerBase
{
    private readonly IDayThemeService _dayThemeService = dayThemeService;

    [HttpGet("today")]
    public async Task<IActionResult> GetToday(CancellationToken ct)
    {
        var dto = await _dayThemeService.GetTodayAsync(ct);
        return Ok(dto);
    }
}
