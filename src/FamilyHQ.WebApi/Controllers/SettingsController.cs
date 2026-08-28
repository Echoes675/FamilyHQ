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
    private readonly ITimeZoneService _timeZoneService;

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
        ICurrentUserService currentUser,
        ITimeZoneService timeZoneService)
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
        _timeZoneService = timeZoneService;
    }

    [HttpGet("location")]
    public async Task<IActionResult> GetLocation(CancellationToken ct)
    {
        // FHQ-179: there is no auto-detection fallback. The only automatic source was an ip-api call
        // made from THIS container, so it resolved the hosting VPS — a family in Derry with no saved
        // location was shown a German city, labelled "Auto", as though FamilyHQ had worked out where
        // they were. There is no correct value to substitute: a server-side IP lookup describes the
        // server, in every deployment. 404 means "none saved", and the client renders an empty state.
        var userId = _currentUser.UserId!;
        var setting = await _locationRepo.GetAsync(userId, ct);

        return setting is null
            ? NotFound()
            : Ok(new LocationSettingDto(setting.PlaceName, IsAutoDetected: false));
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
        // Diagnostic log for fix/weather-refresh-race: pair this with the
        // userId emitted in WeatherController's 409 body to confirm the save
        // and the refresh resolved the same identity from the JWT.
        // FHQ-166: the place name is the family's home address and is not what this line is for —
        // the user id is the whole point of the comparison.
        _logger.LogInformation("SaveLocation entry: userId={UserId}", userId);
        await _locationRepo.UpsertAsync(userId, new LocationSetting
        {
            PlaceName = request.PlaceName,
            Latitude = lat,
            Longitude = lon,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

        // Recalculating for a kiosk whose location was just cleared is a no-op — no location, no
        // boundaries — so the same two lines serve both saving and deleting.
        await _dayThemeService.RecalculateForTodayAsync(userId, ct);

        // FHQ-177: a bare signal, not a period. The theme is per-kiosk, so each client re-reads its
        // own; pushing this kiosk's period to Clients.All would retheme every other kiosk to match it.
        await _hubContext.Clients.All.SendAsync("ThemeChanged", ct);

        await _scheduler.TriggerRecalculationAsync();

        await _weatherRefreshService.RefreshAsync(userId, ct);

        // Re-resolve the auto timezone from the new location, unless the user set one explicitly.
        await _timeZoneService.RepersistAutoIfNotExplicitAsync(ct);

        return Ok(new LocationSettingDto(request.PlaceName, IsAutoDetected: false));
    }

    [HttpDelete("location")]
    public async Task<IActionResult> DeleteLocation(CancellationToken ct)
    {
        var userId = _currentUser.UserId!;
        await _locationRepo.DeleteAsync(userId, ct);

        // Recalculating for a kiosk whose location was just cleared is a no-op — no location, no
        // boundaries — so the same two lines serve both saving and deleting.
        await _dayThemeService.RecalculateForTodayAsync(userId, ct);

        // FHQ-177: a bare signal, not a period. The theme is per-kiosk, so each client re-reads its
        // own; pushing this kiosk's period to Clients.All would retheme every other kiosk to match it.
        await _hubContext.Clients.All.SendAsync("ThemeChanged", ct);

        await _scheduler.TriggerRecalculationAsync();

        await _weatherRefreshService.RefreshAsync(userId, ct);

        // Re-resolve the auto timezone after clearing the location, unless explicitly set.
        await _timeZoneService.RepersistAutoIfNotExplicitAsync(ct);

        return NoContent();
    }

    [HttpGet("display")]
    public async Task<IActionResult> GetDisplay(CancellationToken ct)
    {
        var userId = _currentUser.UserId!;
        var setting = await _displayRepo.GetAsync(userId, ct);
        if (setting is null)
            return Ok(new DisplaySettingDto(1.0, false, 15, "auto"));

        return Ok(new DisplaySettingDto(
            setting.SurfaceMultiplier,
            setting.OpaqueSurfaces,
            setting.TransitionDurationSecs,
            setting.ThemeSelection));
    }

    [HttpPut("display")]
    public async Task<IActionResult> PutDisplay([FromBody] DisplaySettingDto dto, CancellationToken ct)
    {
        var validator = new DisplaySettingDtoValidator();
        var validation = await validator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var userId = _currentUser.UserId!;
        var existing = await _displayRepo.GetAsync(userId, ct);
        var setting = existing ?? new DisplaySetting();
        setting.SurfaceMultiplier = dto.SurfaceMultiplier;
        setting.OpaqueSurfaces = dto.OpaqueSurfaces;
        setting.TransitionDurationSecs = dto.TransitionDurationSecs;
        setting.ThemeSelection = dto.ThemeSelection;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        // IanaTimeZone is intentionally NOT overwritten here — display settings must not
        // wipe the user's explicit timezone (FHQ-43).

        await _displayRepo.UpsertAsync(userId, setting, ct);

        return Ok(dto);
    }

    [HttpGet("weather")]
    public async Task<IActionResult> GetWeatherSettings(CancellationToken ct)
    {
        var dto = await _weatherService.GetSettingsAsync(ct);
        return Ok(dto);
    }

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

    [HttpGet("timezones")]
    public IActionResult GetTimeZones() => Ok(_timeZoneService.GetAvailableZoneIds());

    [HttpGet("timezone")]
    public async Task<IActionResult> GetTimeZone(CancellationToken ct)
    {
        var userId = _currentUser.UserId!;
        var setting = await _displayRepo.GetAsync(userId, ct);
        var isExplicit = setting?.IanaTimeZone is not null && !setting.IsTimeZoneAutoDetected;
        return Ok(new TimeZoneSettingDto(
            setting?.IanaTimeZone ?? "UTC",
            isExplicit,
            isExplicit ? setting!.IanaTimeZone : null));
    }

    [HttpPut("timezone")]
    public async Task<IActionResult> SetTimeZone([FromBody] SetTimeZoneRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IanaTimeZone) || !_timeZoneService.IsValidZone(request.IanaTimeZone))
            return BadRequest("Unknown IANA timezone.");
        await _timeZoneService.SetExplicitZoneAsync(request.IanaTimeZone, ct);
        return NoContent();
    }

    /// <summary>
    /// FHQ-178: the kiosk reports the zone its own OS is set to, on every load. It is the only
    /// automatic source that describes the family rather than the datacentre. Ignored when an
    /// explicit zone is set, and re-reported each load so a change to the kiosk's timezone
    /// propagates without polling.
    /// </summary>
    [HttpPut("timezone/kiosk")]
    public async Task<IActionResult> SetKioskTimeZone([FromBody] SetTimeZoneRequest request, CancellationToken ct)
    {
        // A kiosk on an old TZDB could report a zone this server cannot resolve. That is not a
        // client error worth surfacing on a wall display — the zone simply stays as it was, and
        // GetSendZoneAsync keeps returning whatever was already there (or null).
        if (string.IsNullOrWhiteSpace(request.IanaTimeZone) || !_timeZoneService.IsValidZone(request.IanaTimeZone))
            return NoContent();

        await _timeZoneService.SetKioskZoneAsync(request.IanaTimeZone, ct);
        return NoContent();
    }

    [HttpDelete("timezone")]
    public async Task<IActionResult> ResetTimeZone(CancellationToken ct)
    {
        await _timeZoneService.ResetToAutoZoneAsync(ct);
        return NoContent();
    }
}
