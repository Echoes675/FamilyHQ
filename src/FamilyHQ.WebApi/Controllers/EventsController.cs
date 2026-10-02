using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly ICalendarEventService _service;
    private readonly ICalendarRepository _calendarRepository;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        ICalendarEventService service,
        ICalendarRepository calendarRepository,
        ICurrentUserService currentUser,
        ILogger<EventsController> logger)
    {
        _service = service;
        _calendarRepository = calendarRepository;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// One event by id. The reminders timeline needs this: a row's event may sit in a month the
    /// dashboard has never loaded, so there is nothing in memory to open.
    /// </summary>
    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> GetEvent(Guid eventId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        // The userId overload scopes the lookup to calendars this user owns, so an event that belongs
        // to someone else comes back null exactly like an id that does not exist at all — and both are
        // reported as a plain 404. A 403 for "exists but isn't yours" would itself leak the event's
        // existence to a caller who otherwise has no way to tell the two cases apart.
        var evt = await _calendarRepository.GetEventAsync(eventId, userId, ct);
        if (evt is null) return NotFound();

        // CalendarEvent has no navigation property to its owner, only the FK — one more lookup, but a
        // single row rather than the whole calendar list this endpoint has no other use for.
        var owner = await _calendarRepository.GetCalendarByIdAsync(evt.OwnerCalendarInfoId, ct);
        return Ok(MapToDto(evt, owner));
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var created = await _service.CreateAsync(request, ct);
        return Created($"/api/events/{created.Id}", MapToDto(created));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.UpdateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var updated = await _service.UpdateAsync(eventId, request, ct);
        return Ok(MapToDto(updated));
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid eventId, CancellationToken ct)
    {
        await _service.DeleteAsync(eventId, ct);
        return NoContent();
    }

    /// <summary>
    /// Updates a recurring series at the given <see cref="RecurrenceScope"/> (FHQ-18). The scope
    /// travels as a query parameter so the body stays the same <see cref="UpdateEventRequest"/> the
    /// single-event channel uses. Member changes are only honoured at AllInSeries by the service.
    /// </summary>
    [HttpPut("{eventId:guid}/recurring")]
    public async Task<IActionResult> UpdateRecurringEvent(
        Guid eventId, [FromQuery] RecurrenceScope scope, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.UpdateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var updated = await _service.UpdateRecurringAsync(eventId, request, scope, ct);
        return Ok(MapToDto(updated));
    }

    /// <summary>Deletes a recurring series at the given <see cref="RecurrenceScope"/> (FHQ-18).</summary>
    [HttpDelete("{eventId:guid}/recurring")]
    public async Task<IActionResult> DeleteRecurringEvent(Guid eventId, [FromQuery] RecurrenceScope scope, CancellationToken ct)
    {
        await _service.DeleteRecurringAsync(eventId, scope, ct);
        return NoContent();
    }

    /// <summary>Replaces the full member list for an event.</summary>
    [HttpPut("{eventId:guid}/members")]
    public async Task<IActionResult> SetMembers(Guid eventId, [FromBody] SetEventMembersRequest request, CancellationToken ct)
    {
        if (request.MemberCalendarInfoIds == null || request.MemberCalendarInfoIds.Count == 0)
            return BadRequest("At least one member is required.");

        var updated = await _service.SetMembersAsync(eventId, request.MemberCalendarInfoIds, ct);
        return Ok(MapToDto(updated));
    }

    // owner is only supplied by GetEvent today: Create/Update/Delete/SetMembers don't have the
    // calendar already loaded, and nothing yet consumes the two owning-calendar fields on their
    // responses. Defaulting to null keeps every one of those call sites compiling unchanged.
    private static CalendarEventDto MapToDto(CalendarEvent e, CalendarInfo? owner = null) => new(
        e.Id,
        e.GoogleEventId,
        e.Title,
        e.Start,
        e.End,
        e.IsAllDay,
        e.Location,
        e.Description,
        e.Members.Select(m => new EventCalendarDto(m.Id, m.DisplayName, m.Color, m.IsShared)).ToList(),
        e.IsRecurring,
        e.RecurrenceRule,
        // Passed straight through, including null. The modal reads this back after a save so it shows
        // what Google actually stored rather than what the kiosk optimistically sent — Google rewrites
        // a reminder silently and still answers 200.
        e.Reminders,
        owner?.Id,
        owner?.DefaultReminders);
}
