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
/// <c>GET /api/reminders/upcoming</c> — the server side of the reminders timeline. Three things are
/// worth testing deliberately rather than incidentally: the 28-day reminder-lead tail on the query
/// window (an event just past the display range can still ping inside it), the one-row-per-ping
/// shape for a shared event (not one row per person), and that the owning calendar's own zone beats
/// the family's configured one when anchoring an all-day reminder.
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

    private static async Task<IReadOnlyList<UpcomingReminderDto>> GetRows(RemindersController sut)
    {
        var result = await sut.GetUpcoming(CancellationToken.None);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeAssignableTo<IReadOnlyList<UpcomingReminderDto>>().Subject;
    }

    [Fact]
    public async Task UpcomingReminders_FilesEveryPingSortedByWhenThePhoneGoesOff()
    {
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        // Returned from the repository out of order — the controller, not the repository, is what
        // must produce the timeline order.
        var later = EventWith(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 10)), title: "Later Ping");
        var earlier = EventWith(new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 10)), title: "Earlier Ping");
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { later, earlier });

        var rows = await GetRows(sut);

        rows.Should().HaveCount(2);
        rows.Select(r => r.TriggerAt).Should().BeInAscendingOrder();
        rows[0].EventTitle.Should().Be("Earlier Ping");
        rows[1].EventTitle.Should().Be("Later Ping");
    }

    [Fact]
    public async Task UpcomingReminders_BreaksATieOnTriggerAtByEventTitle()
    {
        // Two different events whose pings land on the EXACT same instant — nothing but EventTitle
        // can order them, so this is what actually exercises .ThenBy(EventTitle, Ordinal) rather than
        // just happening to pass because TriggerAt alone already decided the order.
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
        rows.Select(r => r.TriggerAt).Distinct().Should().ContainSingle("both pings land on the same instant");
        rows[0].EventTitle.Should().Be("Apple Event");
        rows[1].EventTitle.Should().Be("Zebra Event");
    }

    [Fact]
    public async Task UpcomingReminders_IncludesAnEventStartingAfterTheWindowWhenItsPingFallsInside()
    {
        // Google's maximum lead is 40320 minutes (28 days), so an event starting after the end of
        // next month can still ping inside it. A window that stops at the event start silently loses
        // those.
        var (repository, _, clock, sut) = CreateSut();
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(now);

        var justPastNextMonth = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);
        // The Setup matches ANY window on purpose, so the behaviour assertions below exercise only
        // the ping-admission filter, in isolation from whatever window the controller actually asks
        // for. That isolation is exactly why this test, on its own, cannot catch the query window
        // itself shrinking or losing its 28-day tail — a regression there would still get handed this
        // event by the stub below and would still pass. The Verify afterwards closes that gap by
        // pinning the exact window requested.
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { EventWith(justPastNextMonth, Explicit(("popup", 40320))) });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle("the ping is inside the window even though the event is not");
        rows[0].TriggerAt.Should().Be(justPastNextMonth.AddMinutes(-40320));

        // now = 10 March 00:00Z is the queried start; 1 May 00:00Z is the exclusive "end of next
        // month" display boundary; +28 days is the reminder-lead tail under test. If that tail were
        // shrunk or dropped, this Verify — not the behaviour assertions above — is what fails.
        var expectedStart = new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero);
        var expectedDisplayEnd = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var expectedQueryEnd = expectedDisplayEnd.AddDays(28);
        repository.Verify(r => r.GetEventsAsync(
            It.Is<DateTimeOffset>(d => d == expectedStart),
            It.Is<DateTimeOffset>(d => d == expectedQueryEnd),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAPingThatLandsAfterTheEndOfNextMonth()
    {
        // The EVENT query reaches 28 days past the end of next month (the test above), but the
        // returned PINGS do not: the client's bucketing ends at "next month" and discards anything
        // past it, so a ping out there is pure payload. now = 10 March; "end of next month" is the
        // exclusive start of May, i.e. 2026-05-01T00:00:00Z.
        var (repository, _, clock, sut) = CreateSut();
        clock.SetUtcNow(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));

        // A 0-minute ("at start time") reminder one minute past the display boundary — close enough
        // that only the ping filter, not the event-query window, could be excluding it.
        var start = new DateTimeOffset(2026, 5, 1, 0, 1, 0, TimeSpan.Zero);
        var evt = EventWith(start, Explicit(("popup", 0)));
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });

        var rows = await GetRows(sut);

        rows.Should().BeEmpty("the ping falls after the end of next month, which the event query reaches but the display does not");
    }

    [Fact]
    public async Task UpcomingReminders_ExcludesAPingThatAlreadyFired_ButKeepsALaterPingOnTheSameEvent()
    {
        // One event, two reminders either side of "now" — proves the past-ping exclusion is a filter
        // on each PING, not a reason to drop the whole event.
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

        rows.Should().ContainSingle("the already-fired 90-minute ping is dropped but the still-upcoming 30-minute ping is not");
        rows[0].Minutes.Should().Be(30);
        rows[0].TriggerAt.Should().Be(start.AddMinutes(-30));
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
    public async Task UpcomingReminders_TagsAnInheritedPingAsDefault()
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
        rows[0].Method.Should().Be("popup");
        rows[0].Minutes.Should().Be(15);
        rows[0].TriggerAt.Should().Be(start.AddMinutes(-15));
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
    public async Task UpcomingReminders_ProducesOneRowPerPing_NotOnePerPersonPerPing()
    {
        // The Agenda and Day views project a shared event once per person. Doing that here would
        // double-count the notification: the phone goes off once.
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
        rows[0].TriggerAt.Should().Be(allDayStart.AddMinutes(-60));
    }
}
