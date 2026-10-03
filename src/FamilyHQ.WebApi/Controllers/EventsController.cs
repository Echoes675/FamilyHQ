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
    /// One event by id, for a caller holding an id but no event: an event in a month the dashboard
    /// has never loaded is not in memory to be read. Also the first endpoint to populate the two
    /// owning-calendar fields <c>EventModalLogic.OwningCalendarDefaults</c> reads for an existing
    /// event rather than re-predicting the owner client-side — see <c>MapToDtoAsync</c>.
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

        // Resolves the owner with a single-row lookup, unlike the write actions below, which load the
        // calendars before they write. The difference is deliberate: nothing here has been changed yet,
        // so a failing lookup costs only the error, and fetching every calendar to answer for one event
        // would be the more expensive way round. See MapToDtoAsync.
        return Ok(await MapToDtoAsync(evt, ct));
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        // Loaded before the write so the response needs no I/O once the event exists in Google.
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);

        var created = await _service.CreateAsync(request, ct);
        return Created($"/api/events/{created.Id}", MapToDto(created, calendars));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.UpdateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        // Loaded before the write so the response needs no I/O once the event has changed in Google.
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);

        var updated = await _service.UpdateAsync(eventId, request, ct);
        return Ok(MapToDto(updated, calendars));
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

        // Loaded before the write so the response needs no I/O once the series has changed in Google.
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);

        var updated = await _service.UpdateRecurringAsync(eventId, request, scope, ct);
        return Ok(MapToDto(updated, calendars));
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

        // Loaded before the write so the response needs no I/O once the members have changed in Google.
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);

        var updated = await _service.SetMembersAsync(eventId, request.MemberCalendarInfoIds, ct);
        return Ok(MapToDto(updated, calendars));
    }

    /// <summary>
    /// Resolves the event's owning calendar with a single-row lookup and maps. Used by
    /// <see cref="GetEvent"/> only; the write actions resolve the owner from a list loaded before the
    /// write, through the <see cref="MapToDto(CalendarEvent, IReadOnlyList{CalendarInfo})"/> overload.
    /// Both routes populate the two owning-calendar fields, so those fields mean the same thing on
    /// every response that carries a <see cref="CalendarEventDto"/>: "the server's current answer",
    /// never "populated on this endpoint but silently null on that one". A nullable field can't tell a
    /// caller "not populated here" apart from "no owner", so leaving any response unresolved would be
    /// a trap for whichever future reader is the first to trust a write response's null.
    /// <para>
    /// <see cref="CalendarEvent"/> has no navigation property to its owner, only the FK, so the owner
    /// costs a lookup either way.
    /// </para>
    /// </summary>
    private async Task<CalendarEventDto> MapToDtoAsync(CalendarEvent e, CancellationToken ct)
    {
        var owner = await _calendarRepository.GetCalendarByIdAsync(e.OwnerCalendarInfoId, ct);
        return MapToDto(e, owner);
    }

    /// <summary>
    /// Finds the event's owning calendar among calendars already in hand and maps — no I/O. The write
    /// actions load the calendars before they call the service and map through this afterwards, so
    /// that nothing between a successful write and its response can fail: an event written to Google
    /// and reported as a failed save invites a retry, and a retried create leaves the family a second
    /// event in the Google Calendar app. The round-trip count per request is unchanged — the lookup
    /// moves rather than multiplying — and it mirrors how <c>CalendarEventService</c> already resolves
    /// the owner internally, from all the calendars it loads before writing.
    /// </summary>
    private static CalendarEventDto MapToDto(CalendarEvent e, IReadOnlyList<CalendarInfo> calendars) =>
        MapToDto(e, calendars.FirstOrDefault(c => c.Id == e.OwnerCalendarInfoId));

    private static CalendarEventDto MapToDto(CalendarEvent e, CalendarInfo? owner) => new(
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
