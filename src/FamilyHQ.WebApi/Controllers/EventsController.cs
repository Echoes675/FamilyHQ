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
    private readonly ILogger<EventsController> _logger;

    public EventsController(ICalendarEventService service, ILogger<EventsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var validator = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        var created = await _service.CreateAsync(request, ct);
        return Created($"/api/events/{created.Id}", MapToDto(created));
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator = new Core.Validators.UpdateEventRequestValidator();
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

    [HttpPost("{eventId:guid}/calendars/{calendarId:guid}")]
    public async Task<IActionResult> AddCalendar(Guid eventId, Guid calendarId, CancellationToken ct)
    {
        var updated = await _service.AddCalendarAsync(eventId, calendarId, ct);
        return Ok(MapToDto(updated));
    }

    [HttpDelete("{eventId:guid}/calendars/{calendarId:guid}")]
    public async Task<IActionResult> RemoveCalendar(Guid eventId, Guid calendarId, CancellationToken ct)
    {
        await _service.RemoveCalendarAsync(eventId, calendarId, ct);
        return NoContent();
    }

    [HttpGet("range")]
    public async Task<IActionResult> GetForRange(
        [FromQuery] DateTimeOffset start,
        [FromQuery] DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var events = await _service.GetEventsForRangeAsync(start, end, cancellationToken);
        return Ok(events);
    }

    [HttpPut("{masterEventId}/instances/{recurrenceId}/this")]
    public async Task<IActionResult> UpdateInstance(Guid masterEventId, string recurrenceId, [FromBody] UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateInstanceAsync(masterEventId, recurrenceId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{masterEventId}/instances/{recurrenceId}/from")]
    public async Task<IActionResult> UpdateSeriesFrom(Guid masterEventId, string recurrenceId, [FromBody] UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateSeriesFromAsync(masterEventId, recurrenceId, request, cancellationToken);
        return Ok(result);
    }

    [HttpPut("{masterEventId}/all")]
    public async Task<IActionResult> UpdateAllInSeries(Guid masterEventId, [FromBody] UpdateEventRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.UpdateAllInSeriesAsync(masterEventId, request, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{masterEventId}/instances/{recurrenceId}/this")]
    public async Task<IActionResult> DeleteInstance(Guid masterEventId, string recurrenceId, CancellationToken cancellationToken)
    {
        await _service.DeleteInstanceAsync(masterEventId, recurrenceId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{masterEventId}/instances/{recurrenceId}/from")]
    public async Task<IActionResult> DeleteSeriesFrom(Guid masterEventId, string recurrenceId, CancellationToken cancellationToken)
    {
        await _service.DeleteSeriesFromAsync(masterEventId, recurrenceId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{masterEventId}/all")]
    public async Task<IActionResult> DeleteAllInSeries(Guid masterEventId, CancellationToken cancellationToken)
    {
        await _service.DeleteAllInSeriesAsync(masterEventId, cancellationToken);
        return NoContent();
    }

    private static CalendarEventDto MapToDto(CalendarEvent e) => new(
        e.Id,
        e.GoogleEventId,
        e.Title,
        e.Start,
        e.End,
        e.IsAllDay,
        e.Location,
        e.Description,
        e.Calendars
            .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
            .ToList(),
        e.RecurrenceRule,
        e.RecurrenceId,
        e.IsRecurrenceException,
        e.MasterEventId);
}
