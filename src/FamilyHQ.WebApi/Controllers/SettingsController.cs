using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ILocationSettingRepository _locationRepo;
    private readonly IGeocodingService _geocodingService;
    private readonly IDayThemeService _dayThemeService;
    private readonly IDayThemeScheduler _scheduler;
    private readonly IHubContext<CalendarHub> _hubContext;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        ILocationSettingRepository locationRepo,
        IGeocodingService geocodingService,
        IDayThemeService dayThemeService,
        IDayThemeScheduler scheduler,
        IHubContext<CalendarHub> hubContext,
        ILogger<SettingsController> logger)
    {
        _locationRepo = locationRepo;
        _geocodingService = geocodingService;
        _dayThemeService = dayThemeService;
        _scheduler = scheduler;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet("location")]
    public async Task<IActionResult> GetLocation(CancellationToken ct)
    {
        var setting = await _locationRepo.GetAsync(ct);
        if (setting is null) return NotFound();
        return Ok(new LocationSettingDto(setting.PlaceName, IsAutoDetected: false));
    }

    [HttpPost("location")]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlaceName))
            return BadRequest("PlaceName is required.");

        var (lat, lon) = await _geocodingService.GeocodeAsync(request.PlaceName, ct);

        await _locationRepo.UpsertAsync(new LocationSetting
        {
            PlaceName = request.PlaceName,
            Latitude = lat,
            Longitude = lon,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        await _dayThemeService.RecalculateForTodayAsync(ct);

        var dto = await _dayThemeService.GetTodayAsync(ct);
        await _hubContext.Clients.All.SendAsync("ThemeChanged", dto.CurrentPeriod, ct);

        await _scheduler.TriggerRecalculationAsync();

        return Ok(new LocationSettingDto(request.PlaceName, IsAutoDetected: false));
    }
}
