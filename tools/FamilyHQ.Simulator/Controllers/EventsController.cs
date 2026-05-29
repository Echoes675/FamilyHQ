using System.Globalization;
using FamilyHQ.Core.Calendar.Recurrence;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using FamilyHQ.Simulator.State;
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
    private readonly SyncFailureModeStore _failureStore;
    private readonly OutboundWriteCountStore _writeCountStore;

    public EventsController(SimContext db, ILogger<EventsController> logger, SyncFailureModeStore failureStore, OutboundWriteCountStore writeCountStore)
    {
        _db = db;
        _logger = logger;
        _failureStore = failureStore;
        _writeCountStore = writeCountStore;
    }

    [HttpGet]
    public async Task<IActionResult> ListEvents(
        string calendarId,
        [FromQuery] bool singleEvents = false,
        [FromQuery] string? timeMin = null,
        [FromQuery] string? timeMax = null)
    {
        _logger.LogInformation(
            "[SIM] GET events for calendar: {CalendarId} (singleEvents={SingleEvents}, timeMin={TimeMin}, timeMax={TimeMax})",
            calendarId, singleEvents, timeMin, timeMax);
        var userId = ExtractUserId(Request);

        var injected = SyncFailureResponse.TryBuild(_failureStore.Get(userId ?? string.Empty));
        if (injected is not null)
            return injected;

        var userEventIds = await _db.Events
            .Where(e => e.UserId == userId)
            .Select(e => e.Id)
            .ToListAsync();

        // TODO(Task 16): EventAttendees kept for E2E backward-compat; remove when E2E tests are updated.
        var attendeeEventIds = await _db.EventAttendees
            .Where(a => a.AttendeeCalendarId == calendarId && userEventIds.Contains(a.EventId))
            .Select(a => a.EventId)
            .ToListAsync();

        var events = await _db.Events
            .Where(e => e.UserId == userId && (e.IsDeleted || e.CalendarId == calendarId || attendeeEventIds.Contains(e.Id)))
            .ToListAsync();

        var eventIds = events.Select(e => e.Id).ToList();
        var allAttendees = await _db.EventAttendees
            .Where(a => eventIds.Contains(a.EventId))
            .ToListAsync();

        var attendeesByEvent = allAttendees
            .GroupBy(a => a.EventId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AttendeeCalendarId).ToList());

        // FHQ-18.11: with singleEvents=true (how the app's sync always calls this), a series MASTER
        // is replaced by its expanded per-occurrence INSTANCES bounded by the sync window. This
        // mirrors Google: the bare master row is NOT emitted; each instance carries recurringEventId
        // and the master's content-hash. Without singleEvents the master is returned unchanged so the
        // existing non-recurring contract is untouched.
        var windowStart = ParseGoogleTimeBound(timeMin) ?? DateTimeOffset.MinValue;
        var windowEnd = ParseGoogleTimeBound(timeMax) ?? DateTimeOffset.MaxValue;

        var items = new List<object>();
        foreach (var e in events)
        {
            var eventAttendees = attendeesByEvent.TryGetValue(e.Id, out var list) ? list : new List<string>();

            if (singleEvents && !e.IsDeleted && !string.IsNullOrWhiteSpace(e.RecurrenceRule))
            {
                items.AddRange(ExpandSeriesInstances(e, eventAttendees, windowStart, windowEnd));
            }
            else
            {
                items.Add(MapEventResponse(e, eventAttendees));
            }
        }

        var response = new
        {
            items,
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

        // FHQ-18.11: this is the two-pass master fetch — when the requested id is a series
        // master, return it WITH a recurrence array so the sync can read the RRULE. The app
        // calls events.get(recurringEventId) for each unknown series discovered in the listing.
        return Ok(MapEventResponse(existing, attendeeCalendarIds, includeRecurrence: true));
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
            UserId = userId,
            ContentHash = body.ExtendedProperties?.Private?.GetValueOrDefault("content-hash")
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Created event: {EventId} ({Summary})", newEvent.Id, newEvent.Summary);

        _writeCountStore.Increment(newEvent.Id);
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
        if (body.ExtendedProperties?.Private?.TryGetValue("content-hash", out var hash) == true)
            existing.ContentHash = hash;

        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Updated event: {EventId} ({Summary}) on calendar: {CalendarId}", existing.Id, existing.Summary, existing.CalendarId);

        _writeCountStore.Increment(existing.Id);

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
    public IActionResult PatchEvent(string calendarId, string eventId)
    {
        // No-op: attendee patching is not used in the member-tag model.
        // Kept to avoid 404s from any E2E tests not yet updated (removed in Task 16).
        _logger.LogInformation("[SIM] PATCH attendees (no-op) for event: {EventId}", eventId);
        return Ok();
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

        // Remove attendees before deleting the event
        var attendeeRows = _db.EventAttendees.Where(a => a.EventId == eventId);
        _db.EventAttendees.RemoveRange(attendeeRows);

        _db.Events.Remove(existing);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Deleted event: {EventId}", eventId);

        return NoContent();
    }

    // Sentinel prefix written by the poison-event backdoor. The simulator's own
    // SimulatedEvent.Summary column is HasMaxLength(500) so the poison payload
    // cannot be persisted as-is; we store a short marker and reconstitute the
    // oversize value when the events listing is emitted. This is what makes
    // the WebApi side fail on insert (its CalendarEvent.Title is also 500).
    private const string PoisonEventIdPrefix = "simulated_evt_poison_";
    private const int PoisonSummaryLength = 600;

    private static object MapEventResponse(
        SimulatedEvent e,
        IReadOnlyList<string> attendeeCalendarIds,
        bool includeRecurrence = false) => new
    {
        id          = e.Id,
        status      = e.IsDeleted ? "cancelled" : "confirmed",
        summary     = e.Id.StartsWith(PoisonEventIdPrefix) ? new string('X', PoisonSummaryLength) : e.Summary,
        location    = e.Location,
        description = e.Description,
        start = e.IsAllDay ? (object)new { date = e.StartTime.ToString("yyyy-MM-dd") } : new { dateTime = e.StartTime.ToString("O") },
        end   = e.IsAllDay ? (object)new { date = e.EndTime.ToString("yyyy-MM-dd") }   : new { dateTime = e.EndTime.ToString("O") },
        organizer   = new { email = e.CalendarId, self = true },
        attendees = attendeeCalendarIds.Count > 0
            ? (object)attendeeCalendarIds.Select(cal => new { email = cal, responseStatus = "accepted" }).ToArray()
            : null,
        extendedProperties = e.ContentHash != null
            ? (object)new { @private = new Dictionary<string, string> { ["content-hash"] = e.ContentHash } }
            : null,
        // FHQ-18.11: only the master fetch (events.get) carries the recurrence array. Listing
        // instances never do — they reference the master via recurringEventId instead.
        recurrence = includeRecurrence && !string.IsNullOrWhiteSpace(e.RecurrenceRule)
            ? (object)new[] { e.RecurrenceRule }
            : null
    };

    // FHQ-18.11: expands a series master into the per-occurrence INSTANCES that fall inside the
    // sync window [windowStart, windowEnd). Each instance mirrors what Google emits with
    // singleEvents=true: a synthetic id "{masterId}_{yyyyMMddTHHmmssZ}", recurringEventId pointing
    // at the master, start/end shifted by the occurrence's offset from the master start, the
    // master's content-hash, and status confirmed. The bare master row is intentionally omitted.
    private IEnumerable<object> ExpandSeriesInstances(
        SimulatedEvent master,
        IReadOnlyList<string> attendeeCalendarIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var masterStart = new DateTimeOffset(DateTime.SpecifyKind(master.StartTime, DateTimeKind.Utc));
        var duration = master.EndTime - master.StartTime;

        IReadOnlyList<DateTimeOffset> occurrences;
        try
        {
            occurrences = RecurrenceRuleBuilder
                .Expand(master.RecurrenceRule!, masterStart, windowStart, windowEnd)
                .ToList();
        }
        catch (ArgumentException ex)
        {
            // A malformed seeded RRULE is a test-data error; log and emit no instances rather than
            // failing the whole listing for the calendar.
            _logger.LogWarning(ex,
                "[SIM] Could not expand recurrence rule for master {EventId}: {Rule}",
                master.Id, master.RecurrenceRule);
            yield break;
        }

        foreach (var occurrence in occurrences)
        {
            var occurrenceUtc = occurrence.ToUniversalTime();
            var instanceStart = occurrenceUtc.UtcDateTime;
            var instanceEnd = instanceStart + duration;
            var instanceId = $"{master.Id}_{occurrenceUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}";

            yield return new
            {
                id          = instanceId,
                status      = "confirmed",
                summary     = master.Summary,
                location    = master.Location,
                description = master.Description,
                start = master.IsAllDay ? (object)new { date = instanceStart.ToString("yyyy-MM-dd") } : new { dateTime = instanceStart.ToString("O") },
                end   = master.IsAllDay ? (object)new { date = instanceEnd.ToString("yyyy-MM-dd") }   : new { dateTime = instanceEnd.ToString("O") },
                organizer = new { email = master.CalendarId, self = true },
                attendees = attendeeCalendarIds.Count > 0
                    ? (object)attendeeCalendarIds.Select(cal => new { email = cal, responseStatus = "accepted" }).ToArray()
                    : null,
                extendedProperties = master.ContentHash != null
                    ? (object)new { @private = new Dictionary<string, string> { ["content-hash"] = master.ContentHash } }
                    : null,
                // Links the instance back to its series master so the sync can two-pass-fetch the RRULE.
                recurringEventId = master.Id
            };
        }
    }

    // Parses a Google time bound ("yyyy-MM-ddTHH:mm:ssZ" / ISO 8601) to UTC, or null when absent/unparseable.
    private static DateTimeOffset? ParseGoogleTimeBound(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : null;

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
