using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("api/simulator/backdoor/events")]
public class BackdoorEventsController : ControllerBase
{
    private readonly SimContext _db;

    public BackdoorEventsController(SimContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Adds a new event directly to the simulator — bypasses OAuth, accepts userId in body.
    /// Returns the new event's ID.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddEvent([FromBody] BackdoorEventRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId) || string.IsNullOrWhiteSpace(body.CalendarId))
            return BadRequest("UserId and CalendarId are required.");

        var newEvent = new SimulatedEvent
        {
            Id = "simulated_evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = body.CalendarId,
            Summary = body.Summary,
            Description = body.Description,
            StartTime = body.Start,
            EndTime = body.End,
            IsAllDay = body.IsAllDay,
            UserId = body.UserId
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();

        return Ok(newEvent.Id);
    }

    /// <summary>
    /// Updates the summary of an existing event. Accepts userId in body.
    /// </summary>
    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEvent(string eventId, [FromBody] BackdoorEventRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId))
            return BadRequest("UserId is required.");

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == body.UserId);
        if (existing == null)
            return NotFound();

        existing.Summary = body.Summary;
        await _db.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Soft-deletes an event. Accepts userId as a query string parameter.
    /// The event remains in the database with IsDeleted=true so the next sync
    /// receives a cancelled tombstone and removes it from the WebApi's local store.
    /// </summary>
    [HttpDelete("{eventId}")]
    public async Task<IActionResult> DeleteEvent(string eventId, [FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId query parameter is required.");

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
        if (existing == null)
            return NotFound();

        existing.IsDeleted = true;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    public class PoisonEventRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string CalendarId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Adds an event whose Summary exceeds the WebApi's CalendarEvent.Title
    /// column limit (HasMaxLength(500)). When the next sync runs, EF Core's
    /// SaveChangesAsync throws a DbUpdateException for this single event,
    /// exercising the per-event resilience catch in FHQ-26. Other events in
    /// the same sync still persist successfully.
    /// </summary>
    [HttpPost("poison")]
    public async Task<IActionResult> AddPoisonEvent([FromBody] PoisonEventRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId) || string.IsNullOrWhiteSpace(body.CalendarId))
            return BadRequest("UserId and CalendarId are required.");

        // The simulator's SimulatedEvent.Summary column is HasMaxLength(500), so
        // we cannot persist the 600-char title here — the poison payload is
        // reconstituted by EventsController.MapEventResponse based on the
        // `simulated_evt_poison_` id prefix. Storing "POISONED" is enough to
        // round-trip the row; the listing endpoint emits the oversize summary
        // that the WebApi's CalendarEvent.Title (also 500) rejects.
        var tomorrow = DateTime.UtcNow.Date.AddDays(1);

        var newEvent = new SimulatedEvent
        {
            Id = "simulated_evt_poison_" + Guid.NewGuid().ToString("N"),
            CalendarId = body.CalendarId,
            Summary = "POISONED",
            StartTime = tomorrow,
            EndTime = tomorrow.AddDays(1),
            IsAllDay = true,
            UserId = body.UserId
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();

        return Ok(newEvent.Id);
    }
}
