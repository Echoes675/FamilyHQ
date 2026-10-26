using FamilyHQ.Core.Calendar;
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
    /// and whose own day falls between the household's today and the end of next month — filed by
    /// when the EVENT happens, never by when any one of its reminders fires. The response carries the
    /// household's own today, and the instant that answer stops being true, alongside the rows. An
    /// event with several reminders due at very different lead times (the family's own example: one
    /// every day for the week before it) still produces exactly one row, in the panel holding its own
    /// day, and keeps that row after its last reminder has gone off.
    /// </summary>
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming(CancellationToken ct)
    {
        // The family's FamilyHQ-configured zone, resolved once. A pure DB read behind the service —
        // never ResolveAutoZoneAsync, which calls ip-api and has no business on a read path. Three
        // things below are measured in it: the window's day boundaries, the rollover instant the
        // client refetches at, and every row's own day.
        var familyZone = await _timeZoneService.GetDisplayZoneAsync(ct);

        var now = _timeProvider.GetUtcNow();
        var today = EventWallClock.Of(now, isAllDay: false, familyZone).Date;

        // The last date either panel can hold: the end of next month, inclusive.
        var windowEnd = new DateOnly(today.Year, today.Month, 1).AddMonths(2).AddDays(-1);

        // The query is in INSTANTS and matches on OVERLAP (e.Start < end && e.End > start), while
        // this endpoint files rows by their own local DAY. Those are different questions at the
        // edges, in both directions: an all-day event on `today` is stored at midnight UTC, which is
        // BEFORE local midnight in any negative-offset zone, and a timed event late on `windowEnd`
        // locally is already the next UTC day in any positive one. A day either side is enough slack
        // for both, and costs one day of extra candidates — the row filter below re-asserts the real
        // bounds, as it always has rather than trusting the repository's semantics, which this
        // endpoint does not own.
        var queryStart = LocalMidnight(today.AddDays(-1), familyZone);
        var queryEnd = LocalMidnight(windowEnd.AddDays(2), familyZone);

        var events = await _calendarRepository.GetEventsAsync(queryStart, queryEnd, ct);
        var allCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendarsById = allCalendars.ToDictionary(c => c.Id);

        // Deliberately NOT filtered by IsVisible, unlike CalendarsController.GetEventsForMonth.
        // IsVisible is a preference about the calendar GRID; a calendar hidden from the grid still
        // pings the family's phones. Filtering it out here would make the timeline silently
        // incomplete, which is the one failure this endpoint exists to prevent.
        var rows = events
            .Select(evt => RowFor(evt, calendarsById, familyZone))
            .Where(r => r is not null)
            .Select(r => r!)
            // A DAY comparison, not an instant one. The instant form was the other half of the
            // all-day defect: `evt.Start >= start` dropped an all-day event on the very day it
            // happened, because its stored midnight UTC is earlier than local midnight wherever the
            // offset is negative — so the row vanished from the only panel that could hold it.
            .Where(r => r.EventDate >= today && r.EventDate <= windowEnd)
            .OrderBy(r => new EventWallClock(r.EventDate, r.EventTimeOfDay))
            // Stable order for two events on the same day at the same time.
            .ThenBy(r => r.EventTitle, StringComparer.Ordinal)
            .ToList();

        // ToUniversalTime, not because the instant would otherwise be wrong — LocalMidnight already
        // returns the right moment — but because this field is named for an instant and the client
        // compares it against a clock. LocalMidnight hands back the household's own offset
        // (00:00-04:00 in New York), and anything that reads the DATE half of that, or its .DateTime,
        // gets a wall clock instead of an instant and refetches hours early. Normalising here means
        // there is no second reading of this value to get wrong, which is the whole point of sending
        // it. The two bounds above keep the zoned form deliberately: they are day boundaries measured
        // in the household zone and nothing re-derives a date from them.
        var nextRollover = LocalMidnight(today.AddDays(1), familyZone).ToUniversalTime();

        return Ok(new UpcomingRemindersDto(today, nextRollover, rows));
    }

    /// <summary>
    /// One event's row, or null when this view does not report the event. There are two exclusions,
    /// for two different reasons: the first is deliberate scope — an event that merely inherits its
    /// calendar's reminders is left out although it really will ping, which the filter below explains
    /// — and the second is <see cref="ReminderPingCalculator"/>'s, which computes no pings for an
    /// event that was never synced or whose reminders were explicitly removed.
    /// </summary>
    /// <remarks>
    /// Having already fired is NOT an exclusion. A row is filed by the event's own day, so it has to
    /// survive its own reminders: by the time an event begins, its reminders have usually all gone
    /// off, and dropping the row then would strip today's events off the timeline exactly when the
    /// family most needs them — the rows naming today are the ones a wall display is glanced at for.
    /// "Every ping has fired" is the ordinary state of a row on the day of its event, and
    /// nothing here reads the clock at all — whether a row exists has never depended on when the
    /// request arrived, and now there is no "next reminder" to describe either.
    /// </remarks>
    private UpcomingReminderEventDto? RowFor(
        CalendarEvent evt,
        IReadOnlyDictionary<Guid, CalendarInfo> calendarsById,
        TimeZoneInfo familyZone)
    {
        calendarsById.TryGetValue(evt.OwnerCalendarInfoId, out var owner);

        // The owning calendar's own zone — Google-supplied data, read straight off its calendarList
        // entry — outranks the family's FamilyHQ setting. The setting is a fallback for data Google
        // did not supply, never a substitute for data it did (AGENTS.md's golden rule); using it
        // ahead of a zone Google actually sent would anchor an all-day reminder to the wrong midnight
        // for no reason other than convenience. The calendar list is already loaded for this row's
        // member chips, so consulting it here costs no extra query.
        //
        // The anchor reaches no field of the row: all this endpoint reads of Compute's answer is
        // whether it is empty, and that count is the same whatever zone is passed. It is resolved
        // correctly anyway because Compute's contract takes a zone and a wrong one would make its
        // trigger instants wrong — not because anything below shows them.
        var anchorZone = _timeZoneService.TryResolveZone(owner?.IanaTimeZone) ?? familyZone;

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
        // intent, not for want of a notification. The filter also belongs to this endpoint alone.
        // ReminderPingCalculator's contract is "what will actually ping", and an inheriting event
        // really does ping, so folding this view's narrower scope into it would change what Compute
        // means for every later caller rather than adding a view's opinion to it. Inheritance is
        // also resolved a second time and entirely separately for the event modal, which still
        // shows an inherited reminder for what it is — ReminderPickerModel resolves it from the
        // event's own UseDefault and never calls this calculator. Neither resolution may be narrowed
        // to this one view's scope.
        //
        // No all-day special case is needed, and adding one would be wrong — with one qualifier.
        // An all-day event Google CREATED does not inherit: Google materialises the calendar's
        // defaults onto it as explicit overrides, so a birthday or bin-day event arrives carrying
        // reminders of its own and this filter never sees it. An all-day event CAN still reach the
        // filter, because the kiosk itself sends the revert-to-default body on one: flipping the
        // modal's All-day toggle discards the event's own reminders and asks for the calendar's
        // instead, which goes out as a write whenever the event was not already inheriting. What
        // Google returns for that body has never been observed — the preprod smoke suite records it
        // (RM4) rather than asserting it. If it comes back inheriting, the row is excluded, which is
        // the right answer for an event that is inheriting; so the uniform filter holds either way
        // and there is still nothing to special-case.
        if (evt.Reminders is { UseDefault: true }) return null;

        // Computed for its emptiness alone. Nothing on the row describes a reminder, so no individual
        // ping is carried anywhere — but "will this event ping at all" is still the second exclusion,
        // and Compute is the only thing that answers it across all four stored reminder states.
        //
        // Reminders==null (never synced) and ExplicitlyNone are handled inside Compute — nothing here
        // re-implements those exclusions. The owner's defaults are still handed over even though the
        // filter above leaves Compute no inheriting event to consult them for: what an inheriting
        // event resolves to is Compute's contract to define, not this call site's to pre-empt.
        var pings = ReminderPingCalculator.Compute(evt.Start, evt.IsAllDay, evt.Reminders, owner?.DefaultReminders, anchorZone);
        if (pings.Count == 0) return null;

        var wallClock = EventWallClock.Of(evt.Start, evt.IsAllDay, familyZone);
        return new UpcomingReminderEventDto(
            evt.Id, evt.Title, wallClock.Date, wallClock.Time, BuildMembers(evt, owner));
    }

    /// <summary>
    /// The people chips for one row: every non-shared member calendar, by name, so an event assigned
    /// to people names those people rather than the calendar carrying it.
    /// <para>
    /// When an event carries no member tags at all the owning calendar's own name is used instead,
    /// mirroring the owner fallback in <c>CalendarsController.GetEventsForMonth</c>. That fallback
    /// names the owning calendar WHATEVER it is, a shared household calendar included — it does not
    /// exempt them. Two reasons it is right not to: a chip-less row is a blank region on a wall
    /// display, and every phone in this household signs into one shared Google account, so a reminder
    /// on a shared-calendar event really does ring all of them. Naming the household is the accurate
    /// answer there, not a stand-in for a missing person.
    /// </para>
    /// <para>
    /// Note the deliberate difference from the month view: its <c>EventCalendarDto</c> carries
    /// <c>IsShared</c> so the client can tell a calendar from a person, and
    /// <see cref="ReminderMemberDto"/> does not. Nothing on a reminder row behaves differently for a
    /// shared chip, so carrying the flag here would add a field with no reader — and a field with no
    /// reader is the one most likely to be trusted wrongly later.
    /// </para>
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

    /// <summary>
    /// Midnight on <paramref name="localDate"/> in <paramref name="zone"/>, as an instant.
    /// </summary>
    /// <remarks>
    /// The offset is taken AT that local time rather than at the time of the request, which is what
    /// makes a local day 23 or 25 hours long across a daylight-saving transition instead of always 24.
    /// <para>
    /// Some zones transition AT midnight, so the local time asked about here does not always exist
    /// (Santiago's clocks go from 23:59 straight to 01:00). <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// answers with the offset in force before the transition, which makes this the transition instant
    /// itself — the moment the local date really does become <paramref name="localDate"/>. That is the
    /// answer this is used for, so the skipped-midnight case needs no special handling, only a test
    /// saying so.
    /// </para>
    /// </remarks>
    private static DateTimeOffset LocalMidnight(DateOnly localDate, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }
}
