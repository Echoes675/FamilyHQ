using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/preferences")]
[Authorize]
public class PreferencesController : ControllerBase
{
    private readonly IUserPreferencesService _preferencesService;
    private readonly ICurrentUserService _currentUserService;

    public PreferencesController(
        IUserPreferencesService preferencesService,
        ICurrentUserService currentUserService)
    {
        _preferencesService = preferencesService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        
        var prefs = await _preferencesService.GetPreferencesAsync(userId, cancellationToken);
        return Ok(new
        {
            eventDensity = prefs.EventDensity,
            calendarColumnOrder = prefs.CalendarColumnOrder,
            calendarColorOverrides = prefs.CalendarColorOverrides,
            lastModified = prefs.LastModified
        });
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] UpdatePreferencesRequest request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        
        var prefs = new UserPreferences
        {
            EventDensity = Math.Clamp(request.EventDensity, 1, 3),
            CalendarColumnOrder = request.CalendarColumnOrder,
            CalendarColorOverrides = request.CalendarColorOverrides
        };
        
        var saved = await _preferencesService.SavePreferencesAsync(userId, prefs, cancellationToken);
        return Ok(new
        {
            eventDensity = saved.EventDensity,
            calendarColumnOrder = saved.CalendarColumnOrder,
            calendarColorOverrides = saved.CalendarColorOverrides,
            lastModified = saved.LastModified
        });
    }
}

public record UpdatePreferencesRequest(
    int EventDensity,
    string? CalendarColumnOrder,
    string? CalendarColorOverrides
);