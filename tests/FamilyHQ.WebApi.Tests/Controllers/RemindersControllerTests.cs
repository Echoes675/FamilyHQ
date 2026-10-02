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
    private static readonly Guid OwnerCalendarId = Guid.Parse("00000000-0000-0000-0000-00000000c411");

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

    private static CalendarEvent EventWith(
        DateTimeOffset start,
        EventReminders? reminders,
        bool isAllDay = false,
        string title = "Event",
        Guid? ownerCalendarId = null,
        IEnumerable<string>? members = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "google-" + Guid.NewGuid(),
            Title = title,
            Start = start,
            End = start.AddHours(1),
            IsAllDay = isAllDay,
            Reminders = reminders,
            OwnerCalendarInfoId = ownerCalendarId ?? OwnerCalendarId,
            Members = (members ?? [])
                .Select(name => new CalendarInfo { Id = Guid.NewGuid(), DisplayName = name, IsShared = false })
                .ToList()
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
    public async Task UpcomingReminders_IncludesAnEventStartingAfterTheWindowWhenItsPingFallsInside()
    {
        // Google's maximum lead is 40320 minutes (28 days), so an event starting after the end of
        // next month can still ping inside it. A window that stops at the event start silently loses
        // those.
        var (repository, _, clock, sut) = CreateSut();
        var now = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(now);

        var justPastNextMonth = new DateTimeOffset(2026, 5, 10, 9, 0, 0, TimeSpan.Zero);
        repository.Setup(r => r.GetEventsAsync(
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { EventWith(justPastNextMonth, Explicit(("popup", 40320))) });

        var rows = await GetRows(sut);

        rows.Should().ContainSingle("the ping is inside the window even though the event is not");
        rows[0].TriggerAt.Should().Be(justPastNextMonth.AddMinutes(-40320));
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

        var sharedCalendar = new CalendarInfo { Id = Guid.NewGuid(), DisplayName = "Family", IsShared = true };
        var eoin = new CalendarInfo { Id = Guid.NewGuid(), DisplayName = "Eoin", Color = "#111111", IsShared = false };
        var sarah = new CalendarInfo { Id = Guid.NewGuid(), DisplayName = "Sarah", Color = "#222222", IsShared = false };

        var evt = EventWith(new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero), Explicit(("popup", 30)));
        evt.Members = new List<CalendarInfo> { sharedCalendar, eoin, sarah };
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

        var shared = EventWith(
            new DateTimeOffset(2026, 3, 11, 9, 0, 0, TimeSpan.Zero),
            Explicit(("popup", 30)),
            members: ["Eoin", "Sarah"]);
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
        // Three events, each with their own reminder — if the controller resolved an owner per event
        // via a repository call, this test would see that call three times instead of one.
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
