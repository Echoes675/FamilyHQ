using System.Globalization;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("calendars/{calendarId}/events")]
public class EventsController : ControllerBase
{
    private readonly SimContext _db;

    public EventsController(SimContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> ListEvents(string calendarId)
    {
        Console.WriteLine($"[SIM] GET events for calendar: {calendarId}");
        var userId = ExtractUserId(Request);
        var events = await _db.Events
            .Where(e => e.CalendarId == calendarId && e.UserId == userId)
            .ToListAsync();

        var response = new
        {
            items = events.Select(e => new
            {
                id = e.Id,
                status = "confirmed",
                summary = e.Summary,
                location = e.Location,
                description = e.Description,
                start = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
                end = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = e.EndTime.ToString("O") }
            }),
            nextSyncToken = "simulated_sync_token_" + Guid.NewGuid().ToString("N")
        };

        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent(string calendarId, [FromBody] GoogleEventRequest body)
    {
        Console.WriteLine($"[SIM] POST create event for calendar: {calendarId}");
        if (body == null)
        {
            Console.WriteLine("[SIM] Error: Failed to deserialize request body.");
            return BadRequest();
        }

        var userId = ExtractUserId(Request);
        var newEvent = new SimulatedEvent
        {
            Id = "simulated_evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = body.Summary ?? "New Event",
            Location = body.Location,
            Description = body.Description,
            StartTime = body.Start.DateTime?.ToUniversalTime() ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date, null, DateTimeStyles.AdjustToUniversal) : DateTime.UtcNow),
            EndTime = body.End.DateTime?.ToUniversalTime() ?? (body.End.Date != null ? DateTime.Parse(body.End.Date, null, DateTimeStyles.AdjustToUniversal) : DateTime.UtcNow.AddHours(1)),
            IsAllDay = body.Start.Date != null,
            UserId = userId
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        Console.WriteLine($"[SIM] Created event: {newEvent.Id} ({newEvent.Summary})");

        return Ok(new
        {
            id = newEvent.Id,
            status = "confirmed",
            summary = newEvent.Summary,
            location = newEvent.Location,
            description = newEvent.Description,
            start = newEvent.IsAllDay ? (object)new { date = newEvent.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = newEvent.StartTime.ToString("O") },
            end = newEvent.IsAllDay ? (object)new { date = newEvent.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = newEvent.EndTime.ToString("O") }
        });
    }

    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEvent(string calendarId, string eventId, [FromBody] GoogleEventRequest body)
    {
        Console.WriteLine($"[SIM] PUT update event: {eventId} for calendar: {calendarId}");
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        // Flexibility for seed/evt mismatch
        if (existing == null)
        {
            string altId = eventId.Contains("seed") ? eventId.Replace("seed_", "") : (eventId.StartsWith("evt_") ? eventId.Replace("evt_", "evt_seed_") : eventId);
            existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == altId && e.UserId == userId);
        }

        if (existing == null)
        {
            Console.WriteLine($"[SIM] Error: Event {eventId} not found (tried alt too).");
            return NotFound();
        }

        existing.CalendarId = calendarId;

        if (body == null)
        {
            Console.WriteLine("[SIM] Error: Failed to deserialize request body.");
            return BadRequest();
        }

        existing.Summary = body.Summary ?? existing.Summary;
        existing.Location = body.Location;
        existing.Description = body.Description;
        existing.StartTime = body.Start.DateTime?.ToUniversalTime() ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date, null, DateTimeStyles.AdjustToUniversal) : existing.StartTime);
        existing.EndTime = body.End.DateTime?.ToUniversalTime() ?? (body.End.Date != null ? DateTime.Parse(body.End.Date, null, DateTimeStyles.AdjustToUniversal) : existing.EndTime);
        existing.IsAllDay = body.Start.Date != null;

        await _db.SaveChangesAsync();
        Console.WriteLine($"[SIM] Updated event: {existing.Id} ({existing.Summary}) on calendar: {existing.CalendarId}");

        return Ok(new
        {
            id = existing.Id,
            status = "confirmed",
            summary = existing.Summary,
            location = existing.Location,
            description = existing.Description,
            start = existing.IsAllDay ? (object)new { date = existing.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = existing.StartTime.ToString("O") },
            end = existing.IsAllDay ? (object)new { date = existing.EndTime.ToString("yyyy-MM-dd") } : new { dateTime = existing.EndTime.ToString("O") }
        });
    }

    [HttpDelete("{eventId}")]
    public async Task<IActionResult> DeleteEvent(string calendarId, string eventId)
    {
        Console.WriteLine($"[SIM] DELETE event: {eventId} for calendar: {calendarId}");
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        if (existing == null)
        {
            string altId = eventId.Contains("seed") ? eventId.Replace("seed_", "") : (eventId.StartsWith("evt_") ? eventId.Replace("evt_", "evt_seed_") : eventId);
            existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == altId && e.UserId == userId);
        }

        if (existing == null)
        {
            Console.WriteLine($"[SIM] Error: Event {eventId} not found (tried alt too).");
            return NotFound();
        }

        _db.Events.Remove(existing);
        await _db.SaveChangesAsync();
        Console.WriteLine($"[SIM] Deleted event: {eventId}");

        return NoContent();
    }

    // Token format: "simulated_{userId}_{nonce}"
    private static string? ExtractUserId(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        const string prefix = "Bearer simulated_";
        if (!auth.StartsWith(prefix)) return null;
        var token = auth[prefix.Length..];
        var idx = token.LastIndexOf('_');
        return idx > 0 ? token[..idx] : null;
    }
}
