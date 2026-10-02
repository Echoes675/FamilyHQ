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
    // Google documents 40320 minutes (28 days) as the maximum lead time on a reminders.overrides
    // entry. An event that starts AFTER the end of the window this endpoint reports on can still
    // have a ping that lands INSIDE it — so the events query has to reach 28 days further than the
    // window itself, or those pings are silently lost. It looks like slack added "to be safe"; it is
    // the shortest range that is actually correct, derived straight from Google's own limit rather
    // than picked.
    private const int GoogleMaxReminderLeadDays = 28;

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
    /// Every notification a phone will make between the start of today and the end of next month
    /// (plus the reminder-lead tail described above), across every calendar — filed by the instant
    /// each one actually fires.
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

        // "End of next month" expressed as the exclusive start of the month after that, then pushed
        // out by the reminder-lead tail. AddDays — not a calendar-month add — because the tail is a
        // fixed duration (40320 minutes), the same arithmetic ReminderPingCalculator itself uses.
        var startOfMonthAfterNext = new DateTime(localToday.Year, localToday.Month, 1).AddMonths(2);
        var end = LocalMidnight(startOfMonthAfterNext, familyZone).AddDays(GoogleMaxReminderLeadDays);

        // Exactly two repository calls: every event in the window, and every calendar. The second
        // supplies each event's owner — its zone, its default reminders and its display name — by a
        // dictionary lookup keyed on CalendarEvent.OwnerCalendarInfoId, so no event does a lookup of
        // its own.
        var events = await _calendarRepository.GetEventsAsync(start, end, ct);
        var allCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendarsById = allCalendars.ToDictionary(c => c.Id);

        // Deliberately NOT filtered by IsVisible, unlike CalendarsController.GetEventsForMonth.
        // IsVisible is a preference about the calendar GRID; a calendar hidden from the grid still
        // pings the family's phones. Filtering it out here would make the timeline silently
        // incomplete, which is the one failure this endpoint exists to prevent.
        var rows = events
            .SelectMany(evt => RowsFor(evt, calendarsById, familyZone))
            .OrderBy(r => r.TriggerAt)
            // Stable order when two pings land on the same instant.
            .ThenBy(r => r.EventTitle, StringComparer.Ordinal)
            .ToList();

        return Ok(rows);
    }

    private IEnumerable<UpcomingReminderDto> RowsFor(
        CalendarEvent evt, IReadOnlyDictionary<Guid, CalendarInfo> calendarsById, TimeZoneInfo familyZone)
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
        if (pings.Count == 0) yield break;

        // Computed once per event, not once per ping — a shared event's member chips are identical
        // across every one of its pings.
        var members = BuildMembers(evt, owner);

        foreach (var ping in pings)
        {
            yield return new UpcomingReminderDto(
                ping.TriggerAt, ping.Method, ping.Minutes, ping.IsDefault,
                evt.Id, evt.Title, evt.Start, evt.IsAllDay, members);
        }
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
