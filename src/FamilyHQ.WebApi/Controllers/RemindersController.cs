using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.Reminders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/reminders")]
public class RemindersController : ControllerBase
{
    private readonly ICalendarRepository _calendarRepository;
    private readonly ITimeZoneService _timeZoneService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RemindersController> _logger;

    public RemindersController(
        ICalendarRepository calendarRepository,
        ITimeZoneService timeZoneService,
        TimeProvider timeProvider,
        ILogger<RemindersController> logger)
    {
        _calendarRepository = calendarRepository;
        _timeZoneService = timeZoneService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Every event, across every calendar, that has at least one reminder still due to fire between
    /// now and the end of next month — filed by when the EVENT happens, never by when any one of its
    /// reminders fires. An event with several reminders due at very different lead times (the
    /// family's own example: one every day for the week before it) still produces exactly one row,
    /// in the section containing its own start.
    /// </summary>
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming(CancellationToken ct)
    {
        // A pure DB read — never ResolveAutoZoneAsync, which calls ip-api and has no business on a
        // read path. This is the family's FamilyHQ-configured zone, used for two things below: the
        // window's own day/month boundaries, and — only as a fallback — the all-day anchor for an
        // event whose own calendar reported no zone of its own.
        var sendZoneId = await _timeZoneService.GetSendZoneAsync(ct);
        var familyZone = TryFindZone(sendZoneId) ?? TimeZoneInfo.Utc;

        var now = _timeProvider.GetUtcNow();
        var localToday = TimeZoneInfo.ConvertTime(now, familyZone).Date;
        var start = LocalMidnight(localToday, familyZone);

        // "End of next month" expressed as the exclusive start of the month after that — the far edge
        // of both the query window and the five sections the client buckets rows into.
        var startOfMonthAfterNext = new DateTime(localToday.Year, localToday.Month, 1).AddMonths(2);
        var displayEnd = LocalMidnight(startOfMonthAfterNext, familyZone);

        // No 28-day reminder-lead tail on this query any more. It used to exist because a PING could
        // precede its event into the display window — an event starting just after displayEnd could
        // still carry a reminder whose trigger landed before it, and that ping had to be reachable.
        // Filing by EVENT START removes that reason outright: a row's place in the timeline is decided
        // by evt.Start alone, which the query below already guarantees is inside [start, displayEnd).
        //
        // GetEventsAsync matches on OVERLAP (e.Start < end && e.End > start), not on Start falling
        // inside the window — an event that began before `start` and is still running (e.g. a
        // multi-day event) would otherwise come back with evt.Start before the window's near edge. Both
        // bounds on evt.Start are therefore re-asserted explicitly below rather than trusted to the
        // repository's own query semantics, which this endpoint does not own.
        var events = await _calendarRepository.GetEventsAsync(start, displayEnd, ct);
        var allCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendarsById = allCalendars.ToDictionary(c => c.Id);

        // Deliberately NOT filtered by IsVisible, unlike CalendarsController.GetEventsForMonth.
        // IsVisible is a preference about the calendar GRID; a calendar hidden from the grid still
        // pings the family's phones. Filtering it out here would make the timeline silently
        // incomplete, which is the one failure this endpoint exists to prevent.
        var rows = events
            .Where(evt => evt.Start >= start && evt.Start < displayEnd)
            .Select(evt => RowFor(evt, calendarsById, familyZone, now))
            .OfType<UpcomingReminderEventDto>()
            .OrderBy(r => r.EventStart)
            // Stable order when two events start at the exact same instant.
            .ThenBy(r => r.EventTitle, StringComparer.Ordinal)
            .ToList();

        return Ok(rows);
    }

    /// <summary>
    /// One event's row, or null when it will not produce one. An event produces no row for two
    /// distinct reasons, both ultimately resolved by <see cref="ReminderPingCalculator"/>: it may
    /// compute no pings at all (never synced, explicitly none, or inheriting from a calendar with no
    /// defaults), or every ping it does compute may already have fired. The second case cannot be
    /// decided inside the calculator, which knows nothing of "now" — it is a pure function of the
    /// event and the calendar — so it is applied here instead. Both are the same rule this view has
    /// always followed: it shows what the phone WILL do, not a log of what it already did.
    /// </summary>
    private UpcomingReminderEventDto? RowFor(
        CalendarEvent evt,
        IReadOnlyDictionary<Guid, CalendarInfo> calendarsById,
        TimeZoneInfo familyZone,
        DateTimeOffset now)
    {
        calendarsById.TryGetValue(evt.OwnerCalendarInfoId, out var owner);

        // The owning calendar's own zone — Google-supplied data, read straight off its calendarList
        // entry — outranks the family's FamilyHQ setting. The setting is a fallback for data Google
        // did not supply, never a substitute for data it did (AGENTS.md's golden rule); using it
        // ahead of a zone Google actually sent would anchor an all-day reminder to the wrong midnight
        // for no reason other than convenience. The calendar list is already loaded for the default
        // reminders below, so consulting it here costs no extra query.
        var anchorZone = TryFindZone(owner?.IanaTimeZone) ?? familyZone;

        // Reminders==null (never synced), ExplicitlyNone, and "inherits from a calendar with no
        // defaults" are all handled inside Compute — nothing here re-implements those exclusions.
        var pings = ReminderPingCalculator.Compute(evt.Start, evt.IsAllDay, evt.Reminders, owner?.DefaultReminders, anchorZone);
        if (pings.Count == 0) return null;

        var upcoming = pings.Where(p => p.TriggerAt >= now).ToList();
        if (upcoming.Count == 0) return null;

        var next = upcoming.MinBy(p => p.TriggerAt)!;
        var members = BuildMembers(evt, owner);

        return new UpcomingReminderEventDto(
            evt.Id, evt.Title, evt.Start, evt.IsAllDay,
            upcoming.Count, next.TriggerAt, next.Minutes, next.Method,
            next.IsDefault, members);
    }

    /// <summary>
    /// The people chips for one row: every non-shared member calendar, by name. A shared event names
    /// the people assigned to it, never the shared calendar itself — the phone rings in one person's
    /// pocket, not the household's. Falls back to the owning calendar's own name for a plain event
    /// that carries no member tags at all, mirroring the owner fallback in
    /// <c>CalendarsController.GetEventsForMonth</c>.
    /// </summary>
    private static IReadOnlyList<ReminderMemberDto> BuildMembers(CalendarEvent evt, CalendarInfo? owner)
    {
        var people = evt.Members.Where(m => !m.IsShared).ToList();
        if (people.Count > 0)
        {
            return people.Select(m => new ReminderMemberDto(m.DisplayName, m.Color)).ToList();
        }

        return owner is null ? [] : [new ReminderMemberDto(owner.DisplayName, owner.Color)];
    }

    private static DateTimeOffset LocalMidnight(DateTime localDate, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>, guarded against an id this host's tzdb does
    /// not recognise. Null/blank is the ordinary "nothing stored yet" case and is not logged; a
    /// non-blank id that still fails to resolve is stored data this host cannot read, which is worth
    /// knowing about — but still not a reason to fail the whole endpoint, so it falls through.
    /// </summary>
    private TimeZoneInfo? TryFindZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId)) return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogWarning(ex,
                "Unrecognised IANA zone {IanaZoneId} while building the reminders timeline; falling back.",
                ianaId);
            return null;
        }
    }
}
