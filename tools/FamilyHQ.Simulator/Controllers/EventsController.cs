using System.Globalization;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("calendars/{calendarId}/events")]
public class EventsController : ControllerBase
{
    private readonly SimContext _db;
    private readonly ILogger<EventsController> _logger;

    public EventsController(SimContext db, ILogger<EventsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> ListEvents(string calendarId)
    {
        _logger.LogInformation("[SIM] GET events for calendar: {CalendarId}", calendarId);
        var userId = ExtractUserId(Request);

        var attendeeEventIds = await _db.EventAttendees
            .Where(a => a.AttendeeCalendarId == calendarId)
            .Select(a => a.EventId)
            .ToListAsync();

        var events = await _db.Events
            .Where(e => e.UserId == userId && (e.CalendarId == calendarId || attendeeEventIds.Contains(e.Id)))
            .ToListAsync();

        var eventIds = events.Select(e => e.Id).ToList();
        var allAttendees = await _db.EventAttendees
            .Where(a => eventIds.Contains(a.EventId))
            .ToListAsync();

        var attendeesByEvent = allAttendees
            .GroupBy(a => a.EventId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AttendeeCalendarId).ToList());

        var response = new
        {
            items = events.Select(e =>
            {
                var eventAttendees = attendeesByEvent.TryGetValue(e.Id, out var list) ? list : new List<string>();
                return MapEventResponse(e, eventAttendees);
            }),
            nextSyncToken = "simulated_sync_token_" + Guid.NewGuid().ToString("N")
        };

        return Ok(response);
    }

    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetEvent(string calendarId, string eventId)
    {
        _logger.LogInformation("[SIM] GET event: {EventId} for calendar: {CalendarId}", eventId, calendarId);
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        if (existing == null)
        {
            _logger.LogWarning("[SIM] Event {EventId} not found.", eventId);
            return NotFound(new
            {
                error = new
                {
                    code = 404,
                    message = "Not Found",
                    errors = new[]
                    {
                        new { domain = "calendar", reason = "notFound", message = "Not Found" }
                    }
                }
            });
        }

        var attendeeCalendarIds = await _db.EventAttendees
            .Where(a => a.EventId == eventId)
            .Select(a => a.AttendeeCalendarId)
            .ToListAsync();

        return Ok(MapEventResponse(existing, attendeeCalendarIds));
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent(string calendarId, [FromBody] GoogleEventRequest body)
    {
        _logger.LogInformation("[SIM] POST create event for calendar: {CalendarId}", calendarId);
        if (body == null)
        {
            _logger.LogWarning("[SIM] Failed to deserialize request body for CreateEvent.");
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
        _logger.LogInformation("[SIM] Created event: {EventId} ({Summary})", newEvent.Id, newEvent.Summary);

        return Ok(MapEventResponse(newEvent, new List<string>()));
    }

    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEvent(string calendarId, string eventId, [FromBody] GoogleEventRequest body)
    {
        _logger.LogInformation("[SIM] PUT update event: {EventId} for calendar: {CalendarId}", eventId, calendarId);
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
            _logger.LogWarning("[SIM] Event {EventId} not found for update (tried alt too).", eventId);
            return NotFound();
        }

        existing.CalendarId = calendarId;

        if (body == null)
        {
            _logger.LogWarning("[SIM] Failed to deserialize request body for UpdateEvent {EventId}.", eventId);
            return BadRequest();
        }

        existing.Summary = body.Summary ?? existing.Summary;
        existing.Location = body.Location;
        existing.Description = body.Description;
        existing.StartTime = body.Start.DateTime?.ToUniversalTime() ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date, null, DateTimeStyles.AdjustToUniversal) : existing.StartTime);
        existing.EndTime = body.End.DateTime?.ToUniversalTime() ?? (body.End.Date != null ? DateTime.Parse(body.End.Date, null, DateTimeStyles.AdjustToUniversal) : existing.EndTime);
        existing.IsAllDay = body.Start.Date != null;

        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Updated event: {EventId} ({Summary}) on calendar: {CalendarId}", existing.Id, existing.Summary, existing.CalendarId);

        var attendeeCalendarIds = await _db.EventAttendees
            .Where(a => a.EventId == existing.Id)
            .Select(a => a.AttendeeCalendarId)
            .ToListAsync();

        return Ok(MapEventResponse(existing, attendeeCalendarIds));
    }

    [HttpPost("{eventId}/move")]
    public async Task<IActionResult> MoveEvent(string calendarId, string eventId, [FromQuery] string destination)
    {
        _logger.LogInformation("[SIM] POST move event: {EventId} from calendar: {CalendarId} to: {Destination}", eventId, calendarId, destination);
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        if (existing == null)
        {
            _logger.LogWarning("[SIM] Event {EventId} not found for move.", eventId);
            return NotFound(new
            {
                error = new
                {
                    code = 404,
                    message = "Not Found",
                    errors = new[]
                    {
                        new { domain = "calendar", reason = "notFound", message = "Not Found" }
                    }
                }
            });
        }

        existing.CalendarId = destination;
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Moved event: {EventId} to calendar: {Destination}", existing.Id, destination);

        var attendeeCalendarIds = await _db.EventAttendees
            .Where(a => a.EventId == existing.Id)
            .Select(a => a.AttendeeCalendarId)
            .ToListAsync();

        return Ok(MapEventResponse(existing, attendeeCalendarIds));
    }

    [HttpPatch("{eventId}")]
    public async Task<IActionResult> PatchEvent(string calendarId, string eventId, [FromBody] SimulatorPatchAttendeesRequest body)
    {
        _logger.LogInformation("[SIM] PATCH attendees for event: {EventId} on calendar: {CalendarId}", eventId, calendarId);
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        if (existing == null)
        {
            _logger.LogWarning("[SIM] Event {EventId} not found for patch.", eventId);
            return NotFound(new
            {
                error = new
                {
                    code = 404,
                    message = "Not Found",
                    errors = new[]
                    {
                        new { domain = "calendar", reason = "notFound", message = "Not Found" }
                    }
                }
            });
        }

        var currentAttendees = await _db.EventAttendees
            .Where(a => a.EventId == eventId)
            .ToListAsync();
        _db.EventAttendees.RemoveRange(currentAttendees);

        var newAttendees = body.Attendees
            .Where(a => a.Email != existing.CalendarId)
            .Select(a => new SimulatedEventAttendee { EventId = eventId, AttendeeCalendarId = a.Email })
            .ToList();
        _db.EventAttendees.AddRange(newAttendees);

        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Patched attendees for event: {EventId}", eventId);

        var attendeeCalendarIds = newAttendees.Select(a => a.AttendeeCalendarId).ToList();
        return Ok(MapEventResponse(existing, attendeeCalendarIds));
    }

    [HttpDelete("{eventId}")]
    public async Task<IActionResult> DeleteEvent(string calendarId, string eventId)
    {
        _logger.LogInformation("[SIM] DELETE event: {EventId} for calendar: {CalendarId}", eventId, calendarId);
        var userId = ExtractUserId(Request);

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);

        if (existing == null)
        {
            string altId = eventId.Contains("seed") ? eventId.Replace("seed_", "") : (eventId.StartsWith("evt_") ? eventId.Replace("evt_", "evt_seed_") : eventId);
            existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == altId && e.UserId == userId);
        }

        if (existing == null)
        {
            _logger.LogWarning("[SIM] Event {EventId} not found for delete (tried alt too).", eventId);
            return NotFound(new
            {
                error = new
                {
                    code = 404,
                    message = "Not Found",
                    errors = new[]
                    {
                        new { domain = "calendar", reason = "notFound", message = "Not Found" }
                    }
                }
            });
        }

        _db.Events.Remove(existing);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Deleted event: {EventId}", eventId);

        return NoContent();
    }

    private static object MapEventResponse(SimulatedEvent e, IReadOnlyList<string> attendeeCalendarIds) => new
    {
        id = e.Id,
        status = "confirmed",
        summary = e.Summary,
        location = e.Location,
        description = e.Description,
        start = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
        end   = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd")   } : new { dateTime = e.EndTime.ToString("O")   },
        organizer = new { email = e.CalendarId, self = true },
        attendees = attendeeCalendarIds.Count > 0
            ? (object)attendeeCalendarIds.Select(cal => new { email = cal, responseStatus = "accepted" }).ToArray()
            : null
    };

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
