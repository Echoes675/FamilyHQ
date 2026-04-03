using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Validators;
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
    private readonly IDisplaySettingRepository _displayRepo;
    private readonly IWeatherService _weatherService;
    private readonly IWeatherRefreshService _weatherRefreshService;
    private readonly ICurrentUserService _currentUser;

    public SettingsController(
        ILocationSettingRepository locationRepo,
        IGeocodingService geocodingService,
        IDayThemeService dayThemeService,
        IDayThemeScheduler scheduler,
        IHubContext<CalendarHub> hubContext,
        ILogger<SettingsController> logger,
        IDisplaySettingRepository displayRepo,
        IWeatherService weatherService,
        IWeatherRefreshService weatherRefreshService,
        ICurrentUserService currentUser)
    {
        _locationRepo = locationRepo;
        _geocodingService = geocodingService;
        _dayThemeService = dayThemeService;
        _scheduler = scheduler;
        _hubContext = hubContext;
        _logger = logger;
        _displayRepo = displayRepo;
        _weatherService = weatherService;
        _weatherRefreshService = weatherRefreshService;
        _currentUser = currentUser;
    }

    [HttpGet("location")]
    public async Task<IActionResult> GetLocation(CancellationToken ct)
    {
        var userId = _currentUser.UserId!;
        var setting = await _locationRepo.GetAsync(userId, ct);
        if (setting is null) return NotFound();
        return Ok(new LocationSettingDto(setting.PlaceName, IsAutoDetected: false));
    }

    [HttpPost("location")]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlaceName))
            return BadRequest("PlaceName is required.");

        double lat, lon;
        try
        {
            (lat, lon) = await _geocodingService.GeocodeAsync(request.PlaceName, ct);
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Location not found. Please check the spelling and try again.");
        }

        var userId = _currentUser.UserId!;
        await _locationRepo.UpsertAsync(userId, new LocationSetting
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

        await _weatherRefreshService.RefreshAsync(userId, ct);

        return Ok(new LocationSettingDto(request.PlaceName, IsAutoDetected: false));
    }

    [HttpDelete("location")]
    public async Task<IActionResult> DeleteLocation(CancellationToken ct)
    {
        var userId = _currentUser.UserId!;
        await _locationRepo.DeleteAsync(userId, ct);

        await _dayThemeService.RecalculateForTodayAsync(ct);

        var dto = await _dayThemeService.GetTodayAsync(ct);
        await _hubContext.Clients.All.SendAsync("ThemeChanged", dto.CurrentPeriod, ct);

        await _scheduler.TriggerRecalculationAsync();

        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("display")]
    public async Task<IActionResult> GetDisplay(CancellationToken ct)
    {
        var setting = await _displayRepo.GetAsync(ct);
        if (setting is null)
            return Ok(new DisplaySettingDto(1.0, false, 15));

        return Ok(new DisplaySettingDto(
            setting.SurfaceMultiplier,
            setting.OpaqueSurfaces,
            setting.TransitionDurationSecs));
    }

    [HttpPut("display")]
    public async Task<IActionResult> PutDisplay([FromBody] DisplaySettingDto dto, CancellationToken ct)
    {
        var validator = new DisplaySettingDtoValidator();
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var setting = new DisplaySetting
        {
            SurfaceMultiplier = dto.SurfaceMultiplier,
            OpaqueSurfaces = dto.OpaqueSurfaces,
            TransitionDurationSecs = dto.TransitionDurationSecs,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _displayRepo.UpsertAsync(setting, ct);

        return Ok(dto);
    }

    [AllowAnonymous]
    [HttpGet("weather")]
    public async Task<IActionResult> GetWeatherSettings(CancellationToken ct)
    {
        var dto = await _weatherService.GetSettingsAsync(ct);
        return Ok(dto);
    }

    [AllowAnonymous]
    [HttpPut("weather")]
    public async Task<IActionResult> UpdateWeatherSettings([FromBody] WeatherSettingDto dto, CancellationToken ct)
    {
        var validator = new WeatherSettingDtoValidator();
        var validationResult = await validator.ValidateAsync(dto, ct);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);
        var updated = await _weatherService.UpdateSettingsAsync(dto, ct);
        return Ok(updated);
    }
}
