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
/// that has reminders, filed by <c>EventStart</c>, never one row per reminder — so what is worth
/// testing deliberately here is: an event with several reminders still produces exactly one row
/// naming how many it has and when the next one fires; a row survives its own reminders, because by
/// the time an event starts they have usually all gone off and the family still needs the row;
/// <c>ReminderCount</c> is the total rather than a countdown, which only shows once one has fired;
/// the query window has no reminder-lead tail any more (filing by event start removed the reason for
/// one); and the owning calendar's own zone beats the family's configured one when anchoring an
/// all-day reminder.
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
        Guid? ownerCalendarId = null) =>
        new()
        {
            Id = Guid.Empty,
            GoogleEventId = "google-event-id",
            Title = title,
            Start = start,
            End = start.AddHours(1),
            IsAllDay = isAllDay,
            Reminders = reminders,
            OwnerCalendarInfoId = ownerCalendarId ?? OwnerCalendarId,
            Members = new List<CalendarInfo>()
        };

    private static (
        Mock<ICalendarRepository> Repository,
        Mock<ITimeZoneService> TimeZoneService,
        FakeTimeProvider Clock,
        RemindersController SystemUnderTest) CreateSut()
    {
        var repository = new Mock<ICalendarRepository>();
        var timeZoneService = new Mock<ITimeZoneService>();
        var clock = new FakeTimeProvider();
        var logger = new Mock<ILogger<RemindersController>>();

        // Sane defaults every test can rely on unless it overrides them: one plain owner calendar,
        // no events. GetSendZoneAsync is left unconfigured deliberately — Moq's default for an
        // unconfigured Task<string?> method is a completed task with a null result, which is exactly
        // the "no zone persisted" case this endpoint must fall back to UTC from.
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { DefaultOwnerCalendar() });
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        var sut = new RemindersController(repository.Object, timeZoneService.Object, clock, logger.Object);
        return (repository, timeZoneService, clock, sut);
    }

    private static async Task<IReadOnlyList<UpcomingReminderEventDto>> GetRows(RemindersController sut)
    {
        var result = await sut.GetUpcoming(CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeAssignableTo<IReadOnlyList<UpcomingReminderEventDto>>().Subject;
    }

    [Fact]
    public async Task UpcomingReminders_FilesEveryEventSortedByItsOwnStart()
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
        rows.Select(r => r.EventStart).Should().BeInAscendingOrder();
        rows[0].EventTitle.Should().Be("Earlier Event");
        rows[1].EventTitle.Should().Be("Later Event");
    }

    [Fact]
    public async Task UpcomingReminders_BreaksATieOnEventStartByEventTitle()
    {
        // Two different events that start at the EXACT same instant — nothing but EventTitle can
        // order them, so this is what actually exercises .ThenBy(EventTitle, Ordinal) rather than just
        // happening to pass because EventStart alone already decided the order.
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
        rows.Select(r => r.EventStart).Distinct().Should().ContainSingle("both events start at the same instant");
        rows[0].EventTitle.Should().Be("Apple Event");
        rows[1].EventTitle.Should().Be("Zebra Event");
    }

    [Fact]
    public async Task UpcomingReminders_QueriesOnlyTheDisplayWindow_WithNoReminderLeadTail()
    {
        // Filing by EVENT START (rather than by each ping's own trigger instant) removes the reason a
        // 28-day reminder-lead tail used to exist on this query: a ping could previously precede its
        // event into the display window, but a ROW's place in the timeline no longer depends on any
        // ping's trigger instant at all, only on evt.Start — which this query already bounds. now = 10
        // March 00:00Z is the queried start; 1 May 00:00Z is the exclusive "end of next month" bound.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        await GetRows(sut);

        var expectedStart = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var expectedEnd = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
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
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        var startedYesterday = EventWith(new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 0)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { startedYesterday });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty("the event's own start is before the window this view reports on");
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAnEventStartingOnOrAfterTheEndOfNextMonth()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        // Exactly on the exclusive "end of next month" boundary — 1 May 00:00Z.
        var onTheBoundary = EventWith(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), Explicit(("popup", 0)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { onTheBoundary });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty("the event starts exactly on the exclusive end-of-next-month boundary");
    }

    [Fact]
    public async Task UpcomingReminders_WhenOneOfTwoRemindersHasAlreadyFired_CountsBothAndNamesTheOneStillToCome()
    {
        // The test that pins ReminderCount as the TOTAL rather than how many are left. It needs one
        // reminder either side of "now" to say anything at all: before any of them fire the total and
        // the remaining count are the same number, so a test written at that moment passes whichever
        // the controller reports. Here they differ — two reminders, one fired — and only the total is
        // the number the family set in Google.
        var (repository, _, clock, sut) = CreateSut();
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(now);

        // Start is an hour after "now". The 90-minute-before reminder already fired at 08:30 (30
        // minutes before "now"); the 30-minute-before reminder is still ahead, at 09:30.
        var start = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        var evt = EventWith(start, Explicit(("popup", 90), ("popup", 30)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].ReminderCount.Should().Be(2, "the event carries two reminders; one of them having fired does not unset it");
        rows[0].NextReminderMinutes.Should().Be(30, "only the still-upcoming reminder can be the next one");
        rows[0].NextReminderAt.Should().Be(start.AddMinutes(-30));
    }

    [Fact]
    public async Task UpcomingReminders_WhenEveryReminderHasAlreadyFired_KeepsTheRowWithNoNextReminder()
    {
        // The row is filed by the EVENT's start, so it has to outlive its own reminders: on the day of
        // an event they have usually all gone off, and an 18:00 event with one reminder two hours
        // before would otherwise drop off the kiosk at 16:00 — vanishing from Today exactly when the
        // family most needs it there. The row stays and reports no next reminder instead.
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
        rows[0].NextReminderAt.Should().BeNull("every one of its reminders has already fired");
        rows[0].NextReminderMinutes.Should().BeNull();
        rows[0].NextReminderMethod.Should().BeNull();
        rows[0].ReminderCount.Should().Be(1, "the count is what the event carries, not what is left to come");
    }

    [Fact]
    public async Task UpcomingReminders_WhenEveryReminderHasFiredOnAnInheritingEvent_StillTagsTheRowAsDefault()
    {
        // IsDefault describes where the event's reminders came FROM, which does not stop being true
        // once they have fired. It is read off the event's resolved reminders rather than off the next
        // ping, so the "default" tag survives into the day of the event.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        var owner = DefaultOwnerCalendar(defaultReminders: Explicit(("popup", 15)));
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        var evt = EventWith(new DateTimeOffset(2026, 3, 10, 9, 10, 0, TimeSpan.Zero), EventReminders.InheritsCalendarDefault);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].NextReminderAt.Should().BeNull("the calendar's 15-minute reminder fired five minutes ago");
        rows[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task UpcomingReminders_AnEventWithSeveralReminders_ProducesOneRowNamingTheCountAndTheNextOne()
    {
        // The family's own motivating case: an event with reminders due at very different lead times
        // (here 30 minutes, 2 hours, and a week before) must still produce exactly ONE row — not one
        // per reminder — naming how many are left and which one fires soonest. The soonest to fire is
        // the one with the LARGEST lead time (it is furthest from the event, so its trigger instant is
        // the earliest), not the smallest.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var evt = EventWith(start, Explicit(("popup", 30), ("popup", 120), ("popup", 10080)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle("three reminders on one event must still file as one row, not three");
        rows[0].ReminderCount.Should().Be(3);
        rows[0].NextReminderMinutes.Should().Be(10080, "the week-ahead reminder fires soonest, being furthest from the event");
        rows[0].NextReminderAt.Should().Be(start.AddMinutes(-10080));
        rows[0].EventStart.Should().Be(start, "the row is filed and described by the EVENT's start, not any reminder's trigger");
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
    public async Task UpcomingReminders_TagsAnInheritedEventAsDefault()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        var owner = DefaultOwnerCalendar(defaultReminders: Explicit(("popup", 15)));
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        var start = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var evt = EventWith(start, EventReminders.InheritsCalendarDefault);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        rows[0].IsDefault.Should().BeTrue();
        rows[0].NextReminderMethod.Should().Be("popup");
        rows[0].NextReminderMinutes.Should().Be(15);
        rows[0].NextReminderAt.Should().Be(start.AddMinutes(-15));
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

    [Fact]
    public async Task UpcomingReminders_WhenNoZoneIsPersisted_UsesUtc()
    {
        // Neither the owning calendar nor the family has a zone on record. The all-day anchor must
        // fall all the way through to UTC rather than throwing or silently picking the host's own
        // local zone.
        var (repository, timeZoneService, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        timeZoneService.Setup(t => t.GetSendZoneAsync(It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var owner = DefaultOwnerCalendar(ianaTimeZone: null);
        repository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { owner });

        var allDayStart = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
        var evt = EventWith(allDayStart, Explicit(("popup", 60)), isAllDay: true);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle();
        // UTC local midnight on 15 July coincides with the stored instant itself (zero offset).
        rows[0].NextReminderAt.Should().Be(allDayStart.AddMinutes(-60));
    }
}
