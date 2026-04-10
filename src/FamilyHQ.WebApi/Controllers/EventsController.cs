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
        var validator  = new Core.Validators.CreateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        try
        {
            var created = await _service.CreateAsync(request, ct);
            return Created($"/api/events/{created.Id}", MapToDto(created));
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var validator  = new Core.Validators.UpdateEventRequestValidator();
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
            return BadRequest(validation.Errors);

        try
        {
            var updated = await _service.UpdateAsync(eventId, request, ct);
            return Ok(MapToDto(updated));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found")) { return NotFound(ex.Message); }
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid eventId, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(eventId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found")) { return NotFound(ex.Message); }
    }

    /// <summary>Replaces the full member list for an event.</summary>
    [HttpPut("{eventId:guid}/members")]
    public async Task<IActionResult> SetMembers(Guid eventId, [FromBody] SetEventMembersRequest request, CancellationToken ct)
    {
        if (request.MemberCalendarInfoIds == null || request.MemberCalendarInfoIds.Count == 0)
            return BadRequest("At least one member is required.");

        try
        {
            var updated = await _service.SetMembersAsync(eventId, request.MemberCalendarInfoIds, ct);
            return Ok(MapToDto(updated));
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found")) { return NotFound(ex.Message); }
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
        e.Members.Select(m => new EventCalendarDto(m.Id, m.DisplayName, m.Color, m.IsShared)).ToList());
}
