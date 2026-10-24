using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

/// <summary>
/// <c>GET /api/reminders/upcoming</c> — the server side of the reminders timeline. A row is one EVENT
/// whose reminders were set ON THE EVENT, filed by <c>EventDate</c>, never one row per reminder, and
/// carrying nothing that describes those reminders. What is worth testing deliberately here is
/// therefore all about WHICH events produce a row and WHICH DAY the server files it under: an event
/// that merely inherits its calendar's usual reminders yields none at all, while one carrying
/// overrides of its own on that same calendar still does; an event with several reminders still
/// produces exactly one; a row survives its own reminders, because by the time an event starts they
/// have usually all gone off and the family still needs the row; and the query window has no
/// reminder-lead tail any more, filing by the event's own day having removed the reason for one.
/// <para>
/// The day is the server's answer now, not the client's, so the cases below are mostly about zones:
/// the household's own "today" and the instant it stops being today travel with the rows, and both
/// they and every row's day are measured in the zone FamilyHQ has stored rather than in UTC or in
/// whatever zone the kiosk happens to be set to.
/// </para>
/// </summary>
public class RemindersControllerTests
{
    // Static, predictable GUIDs throughout this file (per .agent/skills/testing-standards/SKILL.md)
    // rather than Guid.NewGuid() — matches CalendarsControllerTests.cs's CalAId/CalBId/EventId style.
    // No test in this file asserts on an event's own Id, so every CalendarEvent built by EventWith
    // shares Guid.Empty; the named people/calendar ids below exist only because their DisplayName
    // needs to be distinguishable, not because their Id is ever compared.
    private static readonly Guid OwnerCalendarId = Guid.Parse("00000000-0000-0000-0000-00000000c411");
    private static readonly Guid EoinCalendarId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SarahCalendarId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SharedFamilyCalendarId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static EventReminders Explicit(params (string Method, int Minutes)[] overrides) =>
        EventReminders.Explicit(overrides.Select(o => new EventReminder(o.Method, o.Minutes)));

    private static CalendarInfo DefaultOwnerCalendar(
        EventReminders? defaultReminders = null,
        string? ianaTimeZone = null,
        string displayName = "Family Calendar",
        string? color = null) =>
        new()
        {
            Id = OwnerCalendarId,
            DisplayName = displayName,
            Color = color,
            IanaTimeZone = ianaTimeZone,
            DefaultReminders = defaultReminders,
            IsShared = false
        };

    // Factory METHODS, not shared static fields — CalendarInfo is a mutable class, and a shared
    // instance could pick up a mutation from one test and leak it into another. A fresh instance
    // with the same deterministic Id every call keeps both properties.
    private static CalendarInfo Eoin() => new() { Id = EoinCalendarId, DisplayName = "Eoin", Color = "#111111", IsShared = false };
    private static CalendarInfo Sarah() => new() { Id = SarahCalendarId, DisplayName = "Sarah", Color = "#222222", IsShared = false };
    private static CalendarInfo SharedFamilyCalendar() => new() { Id = SharedFamilyCalendarId, DisplayName = "Family", IsShared = true };

    private static CalendarEvent EventWith(
        DateTimeOffset start,
        EventReminders? reminders,
        bool isAllDay = false,
        string title = "Event",
        Guid? ownerCalendarId = null,
        DateTimeOffset? end = null) =>
        new()
        {
            Id = Guid.Empty,
            GoogleEventId = "google-event-id",
            Title = title,
            Start = start,
            End = end ?? start.AddHours(1),
            IsAllDay = isAllDay,
            Reminders = reminders,
            OwnerCalendarInfoId = ownerCalendarId ?? OwnerCalendarId,
            Members = new List<CalendarInfo>()
        };

    private static (
        Mock<ICalendarRepository> Repository,
        Mock<ITimeZoneService> TimeZoneService,
        FakeTimeProvider Clock,
        RemindersController SystemUnderTest) CreateSut(TimeZoneInfo? displayZone = null)
    {
        var repository = new Mock<ICalendarRepository>();
        var timeZoneService = new Mock<ITimeZoneService>();
        var clock = new FakeTimeProvider();
        var logger = new Mock<ILogger<RemindersController>>();

        // Sane defaults every test can rely on unless it overrides them: one plain owner calendar, no
        // events, and UTC as the household zone. The zone is configured rather than left to Moq's
        // default, which for a Task-returning method is a null Task — "no zone persisted" is now
        // TimeZoneService's own case (GetDisplayZone_WithNoZonePersisted_IsUtcAndSaysNothing) and not
        // reachable through this mock at all.
        timeZoneService.Setup(s => s.GetDisplayZoneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(displayZone ?? TimeZoneInfo.Utc);
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { DefaultOwnerCalendar() });
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        var sut = new RemindersController(repository.Object, timeZoneService.Object, clock, logger.Object);
        return (repository, timeZoneService, clock, sut);
    }

    private static async Task<UpcomingRemindersDto> GetPayload(RemindersController sut)
    {
        var result = await sut.GetUpcoming(CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<UpcomingRemindersDto>().Subject;
    }

    private static async Task<IReadOnlyList<UpcomingReminderEventDto>> GetRows(RemindersController sut) =>
        (await GetPayload(sut)).Rows;

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
    private static readonly TimeZoneInfo Santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

    [Fact]
    public async Task UpcomingReminders_FilesEveryEventSortedByItsOwnDay()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        // Returned from the repository out of order — the controller, not the repository, is what
        // must produce the timeline order.
        var later = EventWith(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 10)), title: "Later Event");
        var earlier = EventWith(new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 10)), title: "Earlier Event");
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { later, earlier });

        var rows = await GetRows(sut);

        rows.Should().HaveCount(2);
        rows.Select(r => r.EventDate).Should().BeInAscendingOrder();
        rows[0].EventTitle.Should().Be("Earlier Event");
        rows[1].EventTitle.Should().Be("Later Event");
        // Both are timed, so each carries the time the family reads it at as well as the day.
        rows.Should().AllSatisfy(r => r.EventTimeOfDay.Should().Be(new TimeOnly(9, 0)));
    }

    [Fact]
    public async Task UpcomingReminders_BreaksATieOnTheSameDayAndTimeByEventTitle()
    {
        // Two different events on the same day at the same time — nothing but EventTitle can order
        // them, so this is what actually exercises .ThenBy(EventTitle, Ordinal) rather than just
        // happening to pass because the day and time alone already decided the order.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var sameInstant = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var zebra = EventWith(sameInstant, Explicit(("popup", 0)), title: "Zebra Event");
        var apple = EventWith(sameInstant, Explicit(("popup", 0)), title: "Apple Event");
        // Returned "Zebra" first — if the sort didn't apply the tie-break, this input order would
        // survive unchanged and the test would catch it.
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { zebra, apple });

        var rows = await GetRows(sut);

        rows.Should().HaveCount(2);
        rows.Select(r => (r.EventDate, r.EventTimeOfDay)).Distinct()
            .Should().ContainSingle("both events are on the same day at the same time");
        rows[0].EventTitle.Should().Be("Apple Event");
        rows[1].EventTitle.Should().Be("Zebra Event");
    }

    [Fact]
    public async Task UpcomingReminders_QueriesOnlyTheDisplayWindow_WithNoReminderLeadTail()
    {
        // Filing by the event's OWN DAY (rather than by each ping's own trigger instant) removes the
        // reason a 28-day reminder-lead tail used to exist on this query: a ping could previously
        // precede its event into the display window, but a ROW's place in the timeline no longer
        // depends on any ping's trigger instant at all, only on the day the event is on.
        //
        // What the query IS widened by is exactly one day at each end, and no more. The repository
        // answers in INSTANTS while this endpoint files in local DAYS, and the two disagree by up to
        // a day in either direction — an all-day event on the window's first local day is stored at
        // midnight UTC, earlier than local midnight in a negative-offset zone, and a timed event late
        // on the window's last local day is already the next UTC day in a positive one. The bounds
        // are asserted as the window's own edges plus exactly one day so that widening it "a bit
        // more" to make some other case pass fails here.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        await GetRows(sut);

        // The zone is UTC here, so the window's own edges are 10 March 00:00Z (the household's today)
        // and 1 May 00:00Z (the exclusive end of next month).
        var expectedStart = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var expectedEnd = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        repository.Verify(r => r.GetEventsAsync(
            It.Is<DateTimeOffset>(d => d == expectedStart),
            It.Is<DateTimeOffset>(d => d == expectedEnd),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventThatStartedBeforeTheWindow()
    {
        // GetEventsAsync matches on OVERLAP, not on Start falling inside the window, so an ongoing
        // multi-day event that began before the window can come back from the repository with a Start
        // earlier than it. The controller has to re-assert the lower bound itself rather than trust
        // the repository's own query semantics.
        //
        // Two days back, not one: the query is now deliberately widened by a day at each end, so the
        // day before the window is a legitimate candidate rather than an overlap artefact, and it has
        // a case of its own (UpcomingReminders_AnAllDayEventTheDayBeforeTheWindowOpens_IsNotReturned).
        // Two days back can only reach the repository's answer by overlapping the window, which is
        // the case this one is about — so the fixture is a genuinely multi-day event that is still
        // running, not a one-hour event the stub hands over for no reason the query would allow.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        var startedTwoDaysAgo = EventWith(
            new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 0)),
            end: new DateTimeOffset(2026, 3, 20, 9, 0, 0, TimeSpan.Zero));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { startedTwoDaysAgo });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty("the event's own day is before the window this view reports on");
    }

    [Fact]
    public async Task UpcomingReminders_WhenAnEventStartedEarlierToday_KeepsItsRowForTheRestOfTheDay()
    {
        // The window's near edge is local MIDNIGHT, not "now", which is what keeps an event listed for
        // the whole of its own day: a 09:00 event is still reported at 20:00. Anchoring the near edge to
        // `now` would drop an event out of Today the moment it began — the panel the family looks at
        // most, and the one place a disappearing row is most likely to be read as the event being
        // cancelled. By 20:00 its only reminder has long fired, so this also pins that the two
        // conditions compose: a start in the past AND nothing left pending still produces a row.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 20, 0, 0, TimeSpan.Zero));

        var startedThisMorning = EventWith(
            new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 30)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { startedThisMorning });

        var rows = await GetRows(sut);

        var row = rows.Should().ContainSingle().Subject;
        row.EventDate.Should().Be(new DateOnly(2026, 3, 10),
            "the client files by the event's own day, which is what holds the row under Today all day");
        row.EventTimeOfDay.Should().Be(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventOnTheFirstDayOfTheMonthAfterNext()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        // The first day the window cannot hold: a household today of 10 March makes 30 April the last
        // date either panel has room for, so 1 May is out by one day.
        var dayAfterTheWindow = EventWith(new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 0)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { dayAfterTheWindow });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty("the event is on the first day of the month after next, one day past the window");
    }

    [Fact]
    public async Task UpcomingReminders_WhenEveryReminderHasAlreadyFired_StillProducesARow()
    {
        // The row is filed by the EVENT's own day, so it has to outlive its own reminders: on the day
        // of an event they have usually all gone off, and an 18:00 event with one reminder two hours
        // before would otherwise drop off the kiosk at 16:00 — vanishing from Today exactly when the
        // family most needs it there. The row stays, and reads no differently for it: nothing on a
        // row describes a reminder, so firing has nothing about it left to change.
        var (repository, _, clock, sut) = CreateSut();
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(now);

        // Start is 10 minutes after "now"; its only reminder (15 minutes before) fired 5 minutes ago.
        var start = new DateTimeOffset(2026, 3, 10, 9, 10, 0, TimeSpan.Zero);
        var evt = EventWith(start, Explicit(("popup", 15)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle("the event has a reminder and has not started yet, which is what puts it on the timeline");
        rows[0].EventDate.Should().Be(new DateOnly(2026, 3, 10),
            "the row is filed and described by the EVENT's own day");
        rows[0].EventTimeOfDay.Should().Be(new TimeOnly(9, 10));
    }

    [Fact]
    public async Task UpcomingReminders_AnEventWithSeveralReminders_ProducesExactlyOneRow()
    {
        // The family's own motivating case: an event with reminders due at very different lead times
        // (here 30 minutes, 2 hours, and a week before) must still produce exactly ONE row — not one
        // per reminder — carrying the EVENT's own day, whatever sections those three trigger
        // instants are scattered across. The row has no start on it at all any more, which is what
        // makes one row per event the only shape it can take.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var evt = EventWith(start, Explicit(("popup", 30), ("popup", 120), ("popup", 10080)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle("three reminders on one event must still file as one row, not three");
        rows[0].EventDate.Should().Be(new DateOnly(2026, 3, 10),
            "the row is filed and described by the EVENT's own day, not any reminder's trigger");
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventWhoseRemindersWereNeverSynced()
    {
        // Reminders == null means "not yet synced", not "no reminders" — ReminderPingCalculator
        // already refuses to invent a ping for it. This only confirms the controller does not
        // special-case it into a row of its own.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var evt = EventWith(new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero), reminders: null);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventWhoseRemindersWereRemoved()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var evt = EventWith(new DateTimeOffset(2026, 3, 12, 9, 0, 0, TimeSpan.Zero), EventReminders.ExplicitlyNone);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventThatOnlyInheritsItsCalendarsDefaults()
    {
        // The pin for the whole filter, and the one case that used to produce a row: the calendar HAS
        // defaults, so the event really will ping — it is excluded because nobody set that reminder
        // on the event itself, which is the only kind this view reports.
        //
        // Two inheriting events rather than one, because the exclusion is about where the reminders
        // came FROM and not about whether any of them are still pending: the first event's 15-minute
        // default is still ahead of `now`, the second's fired five minutes ago, and neither may
        // appear. That is why the separately named "still tags an inheriting event as default once
        // every reminder has fired" test this replaces is gone rather than inverted — both moments
        // are now the same single fact.
        //
        // The third event is a CONTROL and not decoration, the same rule this view's E2E absence
        // scenarios follow: asserting an empty list proves nothing on its own, because a broken
        // query window, a mis-set clock or a repository stub that returned nothing would all satisfy
        // it. Exactly one row must survive, and it must be the one carrying reminders of its own.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        var owner = DefaultOwnerCalendar(defaultReminders: Explicit(("popup", 15)));
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        var defaultStillAhead = EventWith(
            new DateTimeOffset(2026, 3, 10, 11, 0, 0, TimeSpan.Zero),
            EventReminders.InheritsCalendarDefault, title: "Checkup");
        var defaultAlreadyFired = EventWith(
            new DateTimeOffset(2026, 3, 10, 9, 10, 0, TimeSpan.Zero),
            EventReminders.InheritsCalendarDefault, title: "Eye Test");
        var ownReminder = EventWith(
            new DateTimeOffset(2026, 3, 10, 13, 0, 0, TimeSpan.Zero),
            Explicit(("popup", 30)), title: "Parents Evening");
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { defaultStillAhead, defaultAlreadyFired, ownReminder });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle(
                "the control event's reminders were set on the event itself, and a surviving row is " +
                "what stops the two exclusions passing for a reason that has nothing to do with them")
            .Which.EventTitle.Should().Be("Parents Evening",
                "neither inheriting event's reminders were set on the event, so this view does not " +
                "report them — whether their calendar's default is still ahead or has already fired");
    }

    [Fact]
    public async Task UpcomingReminders_StillListsAnAllDayEventCarryingTheMaterialisedCalendarDefaults()
    {
        // Why excluding inherited events is narrower than it sounds, pinned so nobody "fixes" the
        // exclusion by special-casing all-day events back in. Google does not let an all-day event
        // inherit: it MATERIALISES the calendar's defaults onto the event as explicit overrides when
        // it is created, so a birthday or bin-day event made on a phone arrives with UseDefault false
        // and overrides of its own. The uniform filter therefore never touches it, and the all-day
        // rows the family actually relies on keep appearing without any special case existing at all.
        //
        // Shaped exactly as Google sends it: the event's overrides are a copy of the calendar's own
        // defaults. If the filter ever grew a "do these match the calendar's list?" comparison
        // instead of reading UseDefault, this is the test that would catch it.
        var (repository, timeZoneService, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        var owner = DefaultOwnerCalendar(defaultReminders: Explicit(("popup", 540)), ianaTimeZone: "Europe/Dublin");
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        var allDayStart = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var evt = EventWith(allDayStart, Explicit(("popup", 540)), isAllDay: true, title: "Bin Day");
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle(
            "Google materialises a calendar's defaults onto an all-day event, so it arrives with " +
            "overrides of its own and the inherited-event filter never applies to it");
        rows[0].EventTitle.Should().Be("Bin Day");

        // An INTERACTION assertion, deliberately, and the only kind available here. The owning
        // calendar's own zone is Google-supplied data and outranks the family's FamilyHQ setting as
        // the anchor for an all-day ping — the golden rule's "a fallback for data Google did not
        // supply, never a substitute for data it did". But the anchor reaches no field of the row:
        // this endpoint reads only whether the ping list is empty, and its length is the same in
        // every zone. So the owner lookup could be dropped, or the ?? inverted to prefer the
        // household zone, and every outcome in this file would be unchanged. Verifying the call is
        // what keeps `ianaTimeZone: "Europe/Dublin"` above a fixture argument that means something.
        // Where the anchor actually lands is pinned in ReminderPingCalculatorTests, which can see it.
        timeZoneService.Verify(s => s.TryResolveZone("Europe/Dublin"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpcomingReminders_NamesEveryPersonOnASharedEvent_NotTheSharedCalendar()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var evt = EventWith(new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 30)));
        evt.Members = new List<CalendarInfo> { SharedFamilyCalendar(), Eoin(), Sarah() };
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].Members.Select(m => m.DisplayName).Should().BeEquivalentTo(new[] { "Eoin", "Sarah" });
    }

    [Fact]
    public async Task UpcomingReminders_FallsBackToTheOwnerCalendarNameWhenNoMembersAreAssigned()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var owner = DefaultOwnerCalendar(displayName: "Eoin", color: "#333333");
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        // No `members:` supplied — a plain event with no member tags at all.
        var evt = EventWith(new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 30)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].Members.Should().ContainSingle();
        rows[0].Members[0].DisplayName.Should().Be("Eoin");
        rows[0].Members[0].Color.Should().Be("#333333");
    }

    [Fact]
    public async Task UpcomingReminders_ProducesOneRowPerEvent_NotOnePerPersonOnASharedEvent()
    {
        // The Agenda and Day views project a shared event once per person. Doing that here would
        // double-count the event — a row is a thing the family can tap once, not a thing per person.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var shared = EventWith(new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 30)));
        shared.Members = new List<CalendarInfo> { Eoin(), Sarah() };
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { shared });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].Members.Select(m => m.DisplayName).Should().BeEquivalentTo(new[] { "Eoin", "Sarah" });
    }

    [Fact]
    public async Task UpcomingReminders_AsksTheRepositoryOnce()
    {
        // Three events, each with their own reminder. Times.Once on GetEventsAsync/GetCalendarsAsync
        // alone would NOT catch an owner resolved per event through some OTHER repository method
        // (e.g. GetCalendarByIdAsync(evt.OwnerCalendarInfoId, ct)) — that regression leaves both of
        // those counts at exactly one. VerifyNoOtherCalls is what actually rules out an N+1: it fails
        // on any invocation this test has not explicitly verified, whatever method it went through.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var events = Enumerable.Range(0, 3)
            .Select(i => EventWith(
                new DateTimeOffset(2026, 3, 10 + i, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 10)), title: $"Event {i}"))
            .ToList();
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);

        await GetRows(sut);

        repository.Verify(r => r.GetEventsAsync(
            It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    // "No zone persisted, so the endpoint still answers" used to be a case here. It moved, with the
    // fallback itself, to TimeZoneServiceTests.GetDisplayZone_WithNoZonePersisted_IsUtcAndSaysNothing:
    // this controller no longer resolves a stored id or falls back to UTC — it asks
    // GetDisplayZoneAsync for a TimeZoneInfo and is handed one. There is no "nothing stored" state
    // reachable through the mocked interface any more, so a case here could only have re-tested the
    // mock. It is named rather than quietly dropped, because deleting coverage silently is how a case
    // stops existing.

    // ── The envelope: the anchor and the rollover instant ────────────────────
    //
    // The client no longer derives "today" at all. It is told, because the server is what knows the
    // household zone — and it is told when that answer stops being true, so a kiosk left running
    // overnight refetches at the right instant instead of polling for a date change it reads off its
    // own clock.

    [Fact]
    public async Task UpcomingReminders_CarriesTheHouseholdsOwnTodayAsTheAnchor()
    {
        var (_, _, clock, sut) = CreateSut(NewYork);
        // 03:00Z on 16 June is 23:00 on the 15th in New York. The anchor is the HOUSEHOLD's date, so
        // a UTC reading of this instant (the 16th) is the wrong answer and the one the kiosk used to
        // give.
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));

        var payload = await GetPayload(sut);

        payload.Today.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task UpcomingReminders_OnAnOrdinaryDay_RollsOverAtTheNextLocalMidnight()
    {
        var (_, _, clock, sut) = CreateSut(NewYork);
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));

        var payload = await GetPayload(sut);

        // Midnight on 16 June in New York is 04:00Z — one hour away from the fixture's instant, not
        // twenty-four.
        payload.NextRolloverUtc.Should().Be(new DateTimeOffset(2026, 6, 16, 4, 0, 0, TimeSpan.Zero));
        // And it goes out AT a zero offset, not as 00:00-04:00. The assertion above cannot tell the
        // two apart — DateTimeOffset.Equals compares instants — but the client does: the date half of
        // 00:00-04:00 is the 16th in local terms while the instant is still the 15th in the
        // household's, so a consumer reading a date off this field would refetch four hours early.
        // One value with two readings that disagree at a boundary is the defect this whole change
        // removes, and it must not come back at the seam the change creates.
        payload.NextRolloverUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task UpcomingReminders_OnADaylightSavingTransitionDay_IsNotTwentyFourHoursAway()
    {
        // 29 March 2026 is 23 hours long in Dublin. A rollover computed as "now plus a day" or as
        // "midnight plus 24 hours" lands an hour late, and the kiosk spends that hour showing
        // yesterday's rows as today's.
        var (_, _, clock, sut) = CreateSut(Dublin);
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero));

        var payload = await GetPayload(sut);

        payload.Today.Should().Be(new DateOnly(2026, 3, 29));
        // Midnight on the 30th in Dublin is 23:00Z on the 29th: the clocks went forward at 01:00Z.
        payload.NextRolloverUtc.Should().Be(new DateTimeOffset(2026, 3, 29, 23, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task UpcomingReminders_InAZoneWhoseLocalMidnightIsSkipped_StillRollsIntoTheNextDay()
    {
        // Santiago's spring-forward happens AT midnight: 2026-09-06 00:00 does not exist there, the
        // clocks go straight from 23:59 on the 5th to 01:00 on the 6th. Asking for the offset at a
        // local time that never occurs is a question with no good answer, so what this pins is the
        // two properties that actually matter — the instant is still in the future, and at it the
        // household's date really is the next one.
        var (_, _, clock, sut) = CreateSut(Santiago);
        var now = new DateTimeOffset(2026, 9, 5, 18, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(now);

        var payload = await GetPayload(sut);

        payload.NextRolloverUtc.Should().BeAfter(now);
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(payload.NextRolloverUtc, Santiago).DateTime)
            .Should().Be(payload.Today.AddDays(1));
    }

    // ── An all-day event at the window's edges, in a negative-offset zone ────
    //
    // The defect this ticket exists for, at the one place it was not merely a wrong label but a
    // missing row.

    [Fact]
    public async Task UpcomingReminders_AnAllDayEventOnTheWindowsFirstDayInANegativeZone_IsStillReturned()
    {
        var (repository, _, clock, sut) = CreateSut(NewYork);
        // 03:00Z on 16 June is 23:00 on the 15th in New York, so the household's today is the 15th and
        // its local midnight was 04:00Z on the 15th. An all-day event on the 15th is stored at
        // 00:00Z on the 15th — four hours EARLIER than that. The instant comparison this replaced
        // dropped the row on the very day the event happened.
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));
        var allDayToday = EventWith(
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero), Explicit(("popup", 540)),
            isAllDay: true, title: "School Photos",
            end: new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { allDayToday });

        var payload = await GetPayload(sut);

        var row = payload.Rows.Should().ContainSingle().Subject;
        row.EventDate.Should().Be(payload.Today);
        row.EventTimeOfDay.Should().BeNull();
    }

    [Fact]
    public async Task UpcomingReminders_AnAllDayEventOnTheWindowsLastDayInANegativeZone_IsStillReturned()
    {
        var (repository, _, clock, sut) = CreateSut(NewYork);
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));
        // 31 July 2026 is the last day of next month from a household today of 15 June.
        var lastDay = EventWith(
            new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero), Explicit(("popup", 540)),
            isAllDay: true, title: "Camp Ends",
            end: new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { lastDay });

        var payload = await GetPayload(sut);

        payload.Rows.Should().ContainSingle().Which.EventDate.Should().Be(new DateOnly(2026, 7, 31));
    }

    [Fact]
    public async Task UpcomingReminders_AnAllDayEventTheDayBeforeTheWindowOpens_IsNotReturned()
    {
        // The other side of the widened query: a day of extra candidates comes back from the
        // repository and must be filtered out here rather than reaching the client, which would show
        // a past row among upcoming ones.
        var (repository, _, clock, sut) = CreateSut(NewYork);
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 16, 3, 0, 0, TimeSpan.Zero));
        var yesterday = EventWith(
            new DateTimeOffset(2026, 6, 14, 0, 0, 0, TimeSpan.Zero), Explicit(("popup", 540)),
            isAllDay: true, title: "Yesterday",
            end: new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { yesterday });

        (await GetPayload(sut)).Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task UpcomingReminders_WithinOneDay_PutsTheAllDayEventFirst()
    {
        // A deliberate change from the order a midnight-UTC instant produced: in a zone at UTC+1 that
        // sorted an all-day event between 00:30 and 02:00. All-day first is what Google's day views
        // do, and it is now a property of the ordering key rather than of what an instant happens to
        // be.
        var (repository, _, clock, sut) = CreateSut(Dublin);
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 15, 6, 0, 0, TimeSpan.Zero));
        var timedEarly = EventWith(
            new DateTimeOffset(2026, 6, 16, 23, 30, 0, TimeSpan.Zero), Explicit(("popup", 10)),
            title: "Half Past Midnight");
        var allDay = EventWith(
            new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero), Explicit(("popup", 540)),
            isAllDay: true, title: "Bin Day",
            end: new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { timedEarly, allDay });

        var rows = await GetRows(sut);

        // 23:30Z on the 16th is 00:30 on the 17th in Dublin during BST, so both are on the 17th.
        rows.Select(r => r.EventTitle).Should().ContainInOrder("Bin Day", "Half Past Midnight");
        rows.Should().AllSatisfy(r => r.EventDate.Should().Be(new DateOnly(2026, 6, 17)));
    }

    [Fact]
    public async Task UpcomingReminders_ATimedEventJustAfterLocalMidnight_IsFiledOnItsOwnLocalDay()
    {
        var (repository, _, clock, sut) = CreateSut(Dublin);
        clock.SetUtcNow(new DateTimeOffset(2026, 6, 15, 6, 0, 0, TimeSpan.Zero));
        var justAfterMidnight = EventWith(
            new DateTimeOffset(2026, 6, 16, 23, 30, 0, TimeSpan.Zero), Explicit(("popup", 10)),
            title: "Night Flight");
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { justAfterMidnight });

        var row = (await GetRows(sut)).Should().ContainSingle().Subject;

        row.EventDate.Should().Be(new DateOnly(2026, 6, 17));
        row.EventTimeOfDay.Should().Be(new TimeOnly(0, 30));
    }
}
