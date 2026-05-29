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
        // Bound the expansion horizon when the caller omits time bounds. The app's INCREMENTAL sync
        // (syncToken-based, webhook-triggered) sends no timeMin/timeMax; expanding an unbounded series
        // (e.g. a "weekly forever" rule with no COUNT/UNTIL) over MinValue..MaxValue would generate
        // instances up to the engine's hard cap (~10k) on every such sync — pathologically slow and
        // the app then upserts them all. Real Google never returns infinite instances; mirror that with
        // a sane default horizon around now (generously covering the dashboard's navigable range).
        var now = DateTimeOffset.UtcNow;
        var windowStart = ParseGoogleTimeBound(timeMin) ?? now.AddMonths(-2);
        var windowEnd = ParseGoogleTimeBound(timeMax) ?? now.AddMonths(12);

        // FHQ-18.11 (Pass 3): exception overrides are stored as rows carrying RecurringEventId (the
        // master they belong to) and OriginalStartTime (the occurrence slot they replace). They are
        // NEVER emitted on their own — when singleEvents=true they are surfaced by their master's
        // expansion in place of the computed occurrence at the matching slot (mirrors Google).
        var overridesByMaster = events
            .Where(e => e.RecurringEventId is not null && e.OriginalStartTime is not null)
            .GroupBy(e => e.RecurringEventId!)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SimulatedEvent>)g.ToList());

        var items = new List<object>();
        foreach (var e in events)
        {
            // Stored exception-override rows are surfaced only through their master's expansion.
            if (singleEvents && e.RecurringEventId is not null && e.OriginalStartTime is not null)
                continue;

            var eventAttendees = attendeesByEvent.TryGetValue(e.Id, out var list) ? list : new List<string>();

            if (singleEvents && !e.IsDeleted && !string.IsNullOrWhiteSpace(e.RecurrenceRule))
            {
                var overrides = overridesByMaster.TryGetValue(e.Id, out var o) ? o : Array.Empty<SimulatedEvent>();
                items.AddRange(ExpandSeriesInstances(e, eventAttendees, windowStart, windowEnd, overrides));
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
            ContentHash = body.ExtendedProperties?.Private?.GetValueOrDefault("content-hash"),
            // FHQ-18.11: events.insert with a recurrence array creates a series MASTER. The first
            // RRULE line is stored so the subsequent reconcile list (singleEvents=true) expands it
            // into per-occurrence instances. A non-recurring insert leaves this null.
            RecurrenceRule = ExtractRrule(body.Recurrence)
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Created event: {EventId} ({Summary})", newEvent.Id, newEvent.Summary);

        _writeCountStore.Increment(userId, newEvent.Id);
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

        // FHQ-18.11 (Pass 3): a PUT to a compound INSTANCE id "{masterId}_{stamp}" with no stored row
        // is the "This event" (ThisOnly) edit — the app writes the single occurrence by its own id.
        // Google turns this into an EXCEPTION override; the simulator stores an override row so the
        // next singleEvents=true list emits it in place of the computed occurrence at that slot.
        if (existing == null
            && body != null
            && TryParseCompoundInstanceId(eventId, out var masterId, out var originalStartUtc))
        {
            var master = await _db.Events.FirstOrDefaultAsync(
                e => e.Id == masterId && e.UserId == userId && e.RecurrenceRule != null);
            if (master != null)
                return await UpsertExceptionOverrideAsync(master, eventId, originalStartUtc, body, userId);
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

        // FHQ-18.11: events.update (PUT) carries a recurrence array only when the event is (or is
        // becoming) a series master — this is the toggle-ON path where a previously non-recurring
        // event gains an RRULE. A non-recurring update omits the field entirely (the client drops
        // nulls), so a null Recurrence leaves the existing rule untouched.
        ApplyRecurrence(existing, body.Recurrence);

        await _db.SaveChangesAsync();
        _logger.LogInformation("[SIM] Updated event: {EventId} ({Summary}) on calendar: {CalendarId}", existing.Id, existing.Summary, existing.CalendarId);

        _writeCountStore.Increment(userId, existing.Id);

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
    public async Task<IActionResult> PatchEvent(string calendarId, string eventId, [FromBody] GoogleEventRequest? body)
    {
        // FHQ-18.11: events.patch is the series-recurrence toggle channel. The app sends ONLY a
        // recurrence array here (PatchSeriesRecurrenceAsync / ClearSeriesRecurrenceAsync):
        //   • ["RRULE:…"] → toggle ON / change the RRULE: set the master's RecurrenceRule so the
        //     event becomes (or stays) a series and the next list expands it.
        //   • []          → toggle OFF / collapse: clear RecurrenceRule so the master collapses to
        //     a single non-recurring event in subsequent lists.
        // A patch with no recurrence field at all is a no-op (the historical attendee-patch case).
        _logger.LogInformation("[SIM] PATCH event: {EventId} for calendar: {CalendarId}", eventId, calendarId);

        if (body?.Recurrence is null)
        {
            // No recurrence field: legacy attendee patch (member-tag model derives members from the
            // description, so there is nothing to update). Return 200 to avoid 404s.
            return Ok();
        }

        var userId = ExtractUserId(Request);
        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
        if (existing == null)
        {
            _logger.LogWarning("[SIM] Event {EventId} not found for recurrence patch.", eventId);
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

        ApplyRecurrence(existing, body.Recurrence);
        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "[SIM] Patched recurrence for event: {EventId} → {Rule}",
            existing.Id, existing.RecurrenceRule ?? "(cleared)");

        _writeCountStore.Increment(userId, existing.Id);

        var attendeeCalendarIds = await _db.EventAttendees
            .Where(a => a.EventId == existing.Id)
            .Select(a => a.AttendeeCalendarId)
            .ToListAsync();

        return Ok(MapEventResponse(existing, attendeeCalendarIds, includeRecurrence: true));
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

        // FHQ-18.11 (Pass 4): a DELETE to a compound INSTANCE id "{masterId}_{stamp}" with no stored
        // row is the "This event" (ThisOnly) delete — the app deletes the single occurrence by its own
        // id. Google records the slot with status "cancelled"; the simulator stores (or flags) a
        // cancellation tombstone so the next singleEvents=true list OMITS that occurrence.
        if (existing == null
            && TryParseCompoundInstanceId(eventId, out var masterId, out var originalStartUtc))
        {
            var master = await _db.Events.FirstOrDefaultAsync(
                e => e.Id == masterId && e.UserId == userId && e.RecurrenceRule != null);
            if (master != null)
                return await CancelInstanceOccurrenceAsync(master, eventId, originalStartUtc, userId);
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

        // FHQ-18.11 (Pass 4): the found row is itself a stored instance OVERRIDE (a prior "This event"
        // edit). Deleting it is still a single-occurrence cancellation — flag the slot cancelled rather
        // than hard-removing the row, so expansion continues to OMIT the slot (mirrors Google: deleting
        // an exception removes the occurrence, it does not resurrect the computed one).
        if (existing.RecurringEventId is not null && existing.OriginalStartTime is not null)
        {
            existing.IsCancelled = true;
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SIM] Cancelled stored override occurrence: {EventId}", eventId);
            return NoContent();
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
        DateTimeOffset windowEnd,
        IReadOnlyList<SimulatedEvent> exceptionOverrides)
    {
        var masterStart = new DateTimeOffset(DateTime.SpecifyKind(master.StartTime, DateTimeKind.Utc));
        var duration = master.EndTime - master.StartTime;

        // FHQ-18.11 (Pass 3): index the master's exception overrides by their original-start slot so a
        // computed occurrence at that slot is replaced by the override (mirrors Google's singleEvents
        // expansion). Keyed on the UTC stamp string so the lookup is offset/kind-insensitive.
        var overridesBySlot = exceptionOverrides.ToDictionary(
            o => DateTime.SpecifyKind(o.OriginalStartTime!.Value, DateTimeKind.Utc)
                .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
            o => o);

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
            var slotStamp = occurrenceUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            // An exception override at this slot replaces the computed occurrence: emit the override's
            // own fields, carrying recurringEventId + originalStartTime (the slot it overrides).
            if (overridesBySlot.TryGetValue(slotStamp, out var ovr))
            {
                // FHQ-18.11 (Pass 4): a cancelled override is a deleted occurrence ("This event"):
                // mirror Google's status="cancelled" by OMITTING the slot from singleEvents output.
                if (ovr.IsCancelled)
                    continue;

                yield return MapInstanceResponse(
                    id: ovr.Id,
                    summary: ovr.Summary,
                    location: ovr.Location,
                    description: ovr.Description,
                    start: ovr.StartTime,
                    end: ovr.EndTime,
                    isAllDay: ovr.IsAllDay,
                    calendarId: master.CalendarId,
                    attendeeCalendarIds: attendeeCalendarIds,
                    contentHash: ovr.ContentHash ?? master.ContentHash,
                    recurringEventId: master.Id,
                    originalStartTimeUtc: occurrenceUtc.UtcDateTime);
                continue;
            }

            var instanceStart = occurrenceUtc.UtcDateTime;
            var instanceEnd = instanceStart + duration;
            var instanceId = $"{master.Id}_{slotStamp}";

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

    // FHQ-18.11 (Pass 3): emits a single expanded/override instance. An override additionally carries
    // originalStartTime (the slot it replaces) so the app's GetEventsAsync maps OriginalStartTime, and
    // recurringEventId so the instance is linked to its series master.
    private static object MapInstanceResponse(
        string id,
        string summary,
        string? location,
        string? description,
        DateTime start,
        DateTime end,
        bool isAllDay,
        string calendarId,
        IReadOnlyList<string> attendeeCalendarIds,
        string? contentHash,
        string recurringEventId,
        DateTime originalStartTimeUtc) => new
    {
        id,
        status      = "confirmed",
        summary,
        location,
        description,
        start = isAllDay ? (object)new { date = start.ToString("yyyy-MM-dd") } : new { dateTime = start.ToString("O") },
        end   = isAllDay ? (object)new { date = end.ToString("yyyy-MM-dd") }   : new { dateTime = end.ToString("O") },
        organizer = new { email = calendarId, self = true },
        attendees = attendeeCalendarIds.Count > 0
            ? (object)attendeeCalendarIds.Select(cal => new { email = cal, responseStatus = "accepted" }).ToArray()
            : null,
        extendedProperties = contentHash != null
            ? (object)new { @private = new Dictionary<string, string> { ["content-hash"] = contentHash } }
            : null,
        recurringEventId,
        // The slot this override replaces. Timed overrides carry dateTime; all-day carry date.
        originalStartTime = isAllDay
            ? (object)new { date = originalStartTimeUtc.ToString("yyyy-MM-dd") }
            : new { dateTime = originalStartTimeUtc.ToString("O") }
    };

    // FHQ-18.11 WRITE side: pulls the first "RRULE:" line out of a Google recurrence array.
    // Google packs RRULE/EXDATE/RDATE lines together; FamilyHQ only persists the RRULE. Returns
    // null when the array is null, empty, or carries no RRULE line.
    private static string? ExtractRrule(IReadOnlyList<string>? recurrence) =>
        recurrence is null
            ? null
            : recurrence.FirstOrDefault(line => line.StartsWith("RRULE:", StringComparison.Ordinal));

    // FHQ-18.11 WRITE side: applies a request's recurrence array to a stored event, mirroring how
    // Google interprets the field on insert/update/patch:
    //   • null array  → field absent: leave the existing rule untouched (a plain non-recurring write).
    //   • empty array → explicit collapse: clear the rule so the event becomes a single occurrence.
    //   • with RRULE  → set/replace the rule so the event is (or stays) a series master.
    private static void ApplyRecurrence(SimulatedEvent target, IReadOnlyList<string>? recurrence)
    {
        if (recurrence is null)
            return;

        target.RecurrenceRule = ExtractRrule(recurrence);
    }

    // FHQ-18.11 (Pass 3): splits a compound INSTANCE id "{masterId}_{yyyyMMddTHHmmssZ}" into its master
    // id and the occurrence's original-start instant. The master id itself may contain underscores
    // (e.g. "simulated_evt_<guid>"), so the stamp is taken as the suffix after the LAST underscore and
    // must parse as the fixed UTC stamp; anything else is not a compound instance id (false).
    private static bool TryParseCompoundInstanceId(string id, out string masterId, out DateTime originalStartUtc)
    {
        masterId = string.Empty;
        originalStartUtc = default;

        var lastUnderscore = id.LastIndexOf('_');
        if (lastUnderscore <= 0 || lastUnderscore == id.Length - 1)
            return false;

        var stamp = id[(lastUnderscore + 1)..];
        if (!DateTime.TryParseExact(
                stamp, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;

        masterId = id[..lastUnderscore];
        originalStartUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    // FHQ-18.11 (Pass 3): stores (or updates) an exception override for a single occurrence of a series.
    // The override row carries the compound instance id, links to its master via RecurringEventId, and
    // records the slot it replaces via OriginalStartTime. Its overridden fields come from the PUT body.
    // The master's RecurrenceRule is intentionally NOT copied onto the override — only the master is a
    // series row, so the override is never itself expanded and is surfaced only through the master.
    private async Task<IActionResult> UpsertExceptionOverrideAsync(
        SimulatedEvent master, string instanceId, DateTime originalStartUtc, GoogleEventRequest body, string? userId)
    {
        var existingOverride = await _db.Events.FirstOrDefaultAsync(e => e.Id == instanceId && e.UserId == userId);

        var isAllDay = body.Start.Date != null;
        var start = body.Start.DateTime?.ToUniversalTime()
                    ?? (body.Start.Date != null ? DateTime.Parse(body.Start.Date, null, DateTimeStyles.AdjustToUniversal) : originalStartUtc);
        var end = body.End.DateTime?.ToUniversalTime()
                  ?? (body.End.Date != null ? DateTime.Parse(body.End.Date, null, DateTimeStyles.AdjustToUniversal) : start.AddHours(1));

        var contentHash = body.ExtendedProperties?.Private?.GetValueOrDefault("content-hash");

        if (existingOverride is null)
        {
            existingOverride = new SimulatedEvent
            {
                Id = instanceId,
                UserId = userId,
                RecurringEventId = master.Id,
                OriginalStartTime = originalStartUtc
            };
            _db.Events.Add(existingOverride);
        }

        existingOverride.CalendarId = master.CalendarId;
        existingOverride.Summary = body.Summary ?? master.Summary;
        existingOverride.Location = body.Location;
        existingOverride.Description = body.Description;
        existingOverride.StartTime = start;
        existingOverride.EndTime = end;
        existingOverride.IsAllDay = isAllDay;
        if (contentHash != null)
            existingOverride.ContentHash = contentHash;

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "[SIM] Stored exception override {InstanceId} for series {MasterId} at slot {Slot:O}.",
            instanceId, master.Id, originalStartUtc);

        _writeCountStore.Increment(userId, instanceId);

        return Ok(MapInstanceResponse(
            id: instanceId,
            summary: existingOverride.Summary,
            location: existingOverride.Location,
            description: existingOverride.Description,
            start: existingOverride.StartTime,
            end: existingOverride.EndTime,
            isAllDay: existingOverride.IsAllDay,
            calendarId: master.CalendarId,
            attendeeCalendarIds: Array.Empty<string>(),
            contentHash: existingOverride.ContentHash ?? master.ContentHash,
            recurringEventId: master.Id,
            originalStartTimeUtc: originalStartUtc));
    }

    // FHQ-18.11 (Pass 4): cancels a single occurrence of a series ("This event" delete). Stores (or
    // flags) a tombstone override row linked to the master via RecurringEventId and carrying the
    // cancelled slot in OriginalStartTime with IsCancelled=true. A prior CONTENT override on the same
    // slot is converted to a cancellation (the occurrence disappears, mirroring Google: deleting an
    // exception removes the slot). The master's RecurrenceRule is never copied — the tombstone is not
    // itself a series row. Expansion omits the slot, so siblings remain and the count drops by one.
    private async Task<IActionResult> CancelInstanceOccurrenceAsync(
        SimulatedEvent master, string instanceId, DateTime originalStartUtc, string? userId)
    {
        var existingOverride = await _db.Events.FirstOrDefaultAsync(e => e.Id == instanceId && e.UserId == userId);

        if (existingOverride is null)
        {
            existingOverride = new SimulatedEvent
            {
                Id = instanceId,
                UserId = userId,
                CalendarId = master.CalendarId,
                Summary = master.Summary,
                StartTime = originalStartUtc,
                EndTime = originalStartUtc,
                IsAllDay = master.IsAllDay,
                RecurringEventId = master.Id,
                OriginalStartTime = originalStartUtc
            };
            _db.Events.Add(existingOverride);
        }

        existingOverride.IsCancelled = true;

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "[SIM] Cancelled occurrence {InstanceId} of series {MasterId} at slot {Slot:O}.",
            instanceId, master.Id, originalStartUtc);

        // Google returns 204 No Content for a successful delete (including a cancelled instance).
        return NoContent();
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
