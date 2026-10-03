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
    /// Every event, across every calendar, whose reminders were deliberately set on the event itself
    /// and which starts between now and the end of next month — filed by when the EVENT happens,
    /// never by when any one of its reminders fires. An event with several reminders due at very
    /// different lead times (the family's own example: one every day for the week before it) still
    /// produces exactly one row, in the section containing its own start, and keeps that row after
    /// its last reminder has gone off.
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
            .Where(r => r is not null)
            .Select(r => r!)
            .OrderBy(r => r.EventStart)
            // Stable order when two events start at the exact same instant.
            .ThenBy(r => r.EventTitle, StringComparer.Ordinal)
            .ToList();

        return Ok(rows);
    }

    /// <summary>
    /// One event's row, or null when this view does not report the event. There are two exclusions,
    /// for two different reasons: the first is deliberate scope — an event that merely inherits its
    /// calendar's reminders is left out although it really will ping, which the filter below explains
    /// — and the second is <see cref="ReminderPingCalculator"/>'s, which computes no pings for an
    /// event that was never synced or whose reminders were explicitly removed.
    /// </summary>
    /// <remarks>
    /// Having already fired is NOT an exclusion. A row is filed by the event's start, so it has to
    /// survive its own reminders: by the time an event begins, its reminders have usually all gone
    /// off, and dropping the row then would empty the Today section exactly when the family most
    /// needs it. "Every ping has fired" is the ordinary state of a row on the day of its event, which
    /// is why <paramref name="now"/> decides only which reminder is described as next — it never
    /// decides whether the row exists.
    /// </remarks>
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
        // for no reason other than convenience. The calendar list is already loaded for this row's
        // member chips, so consulting it here costs no extra query.
        var anchorZone = TryFindZone(owner?.IanaTimeZone) ?? familyZone;

        // The subject of this view is reminders somebody DELIBERATELY set on an event. An event that
        // merely follows its calendar's usual reminders is therefore out of scope — knowingly, and
        // even though its phone really will ping. That makes the timeline under-report what Google
        // will do, which is why the view carries a permanent footnote admitting it: the family chose
        // to own that gap rather than fill the panel with every event nobody asked to be reminded
        // about. Letting inherited events back in would turn that footnote into a lie, so an
        // inherited ping missing from this list is the requirement, not a bug.
        //
        // Checked BEFORE Compute rather than left to fall out of an empty ping list, because an
        // inheriting event on a calendar that HAS defaults does produce pings: it is excluded on
        // intent, not for want of a notification. The filter also belongs to this endpoint alone —
        // ReminderPingCalculator is shared, and has to keep all four reminder states resolvable for
        // the event modal, which still shows an inherited reminder for what it is.
        //
        // No all-day special case is needed, and adding one would be wrong: Google materialises a
        // calendar's defaults onto an all-day event rather than letting it inherit, so a birthday or
        // bin-day event arrives carrying explicit overrides and this filter never sees it.
        if (evt.Reminders is { UseDefault: true }) return null;

        // Reminders==null (never synced) and ExplicitlyNone are handled inside Compute — nothing here
        // re-implements those exclusions. The owner's defaults are still handed over even though the
        // filter above leaves Compute no inheriting event to consult them for: what an inheriting
        // event resolves to is Compute's contract to define, not this call site's to pre-empt.
        var pings = ReminderPingCalculator.Compute(evt.Start, evt.IsAllDay, evt.Reminders, owner?.DefaultReminders, anchorZone);
        if (pings.Count == 0) return null;

        // Null once they have all fired, which the row renders as "all sent" rather than hiding.
        var next = pings.Where(p => p.TriggerAt >= now).MinBy(p => p.TriggerAt);
        var members = BuildMembers(evt, owner);

        return new UpcomingReminderEventDto(
            evt.Id, evt.Title, evt.Start, evt.IsAllDay,
            // The TOTAL, not how many are left: see the DTO's own remarks on why a count that decays
            // as the event approaches is the wrong number to show the family.
            pings.Count, next?.TriggerAt, next?.Minutes, next?.Method, members);
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
