using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.WebApi.Tests.Controllers;

public class CalendarsControllerTests
{
    private static readonly Guid CalAId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CalBId  = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    // ── GetEventsForMonth ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsForMonth_EventWithTwoCalendars_ReturnsCalendarEventDtoWithBothCalendars()
    {
        // Arrange
        var (calendarRepository, systemUnderTest) = CreateSut();

        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var calB = new CalendarInfo { Id = CalBId, DisplayName = "Cal B", Color = "#0000ff", IsVisible = true };

        var evt = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Title = "Shared Event",
            Start = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false,
            Members = new List<CalendarInfo> { calA, calB }
        };

        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA, calB });

        // Act
        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var monthView = ok.Value.Should().BeOfType<MonthViewDto>().Subject;
        var dayEvents = monthView.Days["2026-06-15"];
        // One DTO per event (not per member) — client expands into one lane per member
        dayEvents.Should().HaveCount(1);
        var dto = dayEvents[0].Should().BeOfType<CalendarEventDto>().Subject;
        dto.Members.Should().HaveCount(2);
        dto.Members.Should().Contain(c => c.Id == CalAId);
        dto.Members.Should().Contain(c => c.Id == CalBId);
    }

    [Fact]
    public async Task GetEventsForMonth_RecurringEvent_ProjectsRecurrenceOntoDto()
    {
        // FHQ-18: the grid feed must carry recurrence so the edit modal can pre-populate the
        // picker and route Save/Delete through the scope prompt.
        var (calendarRepository, systemUnderTest) = CreateSut();

        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var evt = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Title = "Standup",
            Start = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false,
            GoogleRecurringEventId = "series-1",
            RecurrenceRule = "RRULE:FREQ=WEEKLY",
            Members = new List<CalendarInfo> { calA }
        };

        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var monthView = ok.Value.Should().BeOfType<MonthViewDto>().Subject;
        var dto = monthView.Days["2026-06-15"][0];
        dto.IsRecurring.Should().BeTrue();
        dto.RecurrenceRule.Should().Be("RRULE:FREQ=WEEKLY");
    }

    [Fact]
    public async Task GetEventsForMonth_InvalidMonth_Returns400()
    {
        // Arrange
        var (_, systemUnderTest) = CreateSut();

        // Act
        var result = await systemUnderTest.GetEventsForMonth(2026, 13, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Which day cell an event lands in ─────────────────────────────────────
    //
    // The grid used to key every cell by the UTC date of the stored instant. For an all-day event
    // that is the right answer — it is the date Google sent — and for a timed one it is wrong in any
    // household zone that is not UTC. Both halves are pinned here, and they have to agree with the
    // reminders row's own day: a tap on a row opens the Day view, which reads these same buckets.

    private static readonly TimeZoneInfo Dublin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
    private static readonly TimeZoneInfo Athens = TimeZoneInfo.FindSystemTimeZoneById("Europe/Athens");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static CalendarEvent GridEvent(
        DateTimeOffset start, DateTimeOffset end, bool isAllDay, CalendarInfo calendar, string title = "Event") =>
        new()
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Title = title,
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            Members = new List<CalendarInfo> { calendar }
        };

    [Fact]
    public async Task GetEventsForMonth_ATimedEventJustAfterLocalMidnight_LandsInItsOwnCellNotTheDayBefore()
    {
        // 23:30Z on 14 June is 00:30 on the 15th in Dublin during BST. The capsule used to render in
        // the 14th's cell carrying the label "12:30 AM" — a row the family could read only by
        // distrusting it.
        var (calendarRepository, systemUnderTest) = CreateSut(Dublin);
        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var evt = GridEvent(
            new DateTimeOffset(2026, 6, 14, 23, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 15, 0, 30, 0, TimeSpan.Zero),
            isAllDay: false, calA, "Night Flight");
        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var monthView = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<MonthViewDto>().Subject;
        monthView.Days.Should().ContainKey("2026-06-15");
        monthView.Days.Should().NotContainKey("2026-06-14");
    }

    [Fact]
    public async Task GetEventsForMonth_AnAllDayEventInANegativeZone_KeepsTheCellGoogleNamed()
    {
        // The half that must NOT change. An all-day event on the 15th is stored at midnight UTC on the
        // 15th because that round-trips byte-for-byte; converting it through New York would read the
        // 14th, which is the defect the reminders view had and the grid never did.
        var (calendarRepository, systemUnderTest) = CreateSut(NewYork);
        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var evt = GridEvent(
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero),
            isAllDay: true, calA, "School Photos");
        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var monthView = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<MonthViewDto>().Subject;
        monthView.Days.Keys.Should().BeEquivalentTo(["2026-06-15"]);
    }

    [Fact]
    public async Task GetEventsForMonth_AMultiDayAllDaySpan_OccupiesEveryDayFromItsFirstToItsInclusiveLast()
    {
        // Google's all-day end is the EXCLUSIVE next-day midnight, so a three-day event ending on the
        // 18th covers the 15th, 16th and 17th — and not the 18th.
        var (calendarRepository, systemUnderTest) = CreateSut(NewYork);
        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var evt = GridEvent(
            new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 18, 0, 0, 0, TimeSpan.Zero),
            isAllDay: true, calA, "Half Term");
        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { evt });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var monthView = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<MonthViewDto>().Subject;
        monthView.Days.Keys.Should().BeEquivalentTo(["2026-06-15", "2026-06-16", "2026-06-17"]);
    }

    [Fact]
    public async Task GetEventsForMonth_AnEventWhoseLocalDayIsInsideTheRangeButWhoseUtcDayIsNot_StillReachesItsCell()
    {
        // The reason the query window is widened a day either side. June 2026's grid starts on Sunday
        // 31 May, so the old query began at 2026-05-31T00:00Z. 22:00Z on 30 May is 01:00 on 31 May in
        // Athens (+03:00), so this event belongs to the grid's first cell although its stored instant
        // is before the old query began — and the repository is asked in INSTANTS. The fake honours
        // the bounds it is given, so an event outside them is genuinely never returned.
        var (calendarRepository, systemUnderTest) = CreateSut(Athens);
        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsVisible = true };
        var evt = GridEvent(
            new DateTimeOffset(2026, 5, 30, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 5, 30, 23, 0, 0, TimeSpan.Zero),
            isAllDay: false, calA, "Night Shift");
        DateTimeOffset askedFrom = default;
        calendarRepository.Setup(r => r.GetEventsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<DateTimeOffset, DateTimeOffset, CancellationToken>((from, _, _) => askedFrom = from)
            .Returns<DateTimeOffset, DateTimeOffset, CancellationToken>((from, to, _) =>
                Task.FromResult<IReadOnlyList<CalendarEvent>>(
                    new[] { evt }.Where(e => e.End > from && e.Start < to).ToList()));
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA });

        var result = await systemUnderTest.GetEventsForMonth(2026, 6, CancellationToken.None);

        var monthView = result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<MonthViewDto>().Subject;
        monthView.Days.Keys.Should().BeEquivalentTo(["2026-05-31"],
            "the event is reached by the widened query and filed once, under its household day.");

        // Pinning the widening itself, deliberately: a future edit that narrows the bounds should fail
        // here loudly rather than only surface as a capsule missing from the grid's edge cell.
        askedFrom.Should().BeOnOrBefore(new DateTimeOffset(2026, 5, 31, 0, 0, 0, TimeSpan.Zero).AddDays(-1));
    }

    // ── GetCalendars ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCalendars_ReturnsMappedEventCalendarDtos()
    {
        // Arrange
        var (calendarRepository, systemUnderTest) = CreateSut();

        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000" };
        var calB = new CalendarInfo { Id = CalBId, DisplayName = "Cal B", Color = "#0000ff" };

        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calA, calB });

        // Act
        var result = await systemUnderTest.GetCalendars(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = ok.Value.Should().BeAssignableTo<IEnumerable<EventCalendarDto>>().Subject.ToList();
        dtos.Should().HaveCount(2);
        dtos.Should().Contain(d => d.Id == CalAId && d.DisplayName == "Cal A");
        dtos.Should().Contain(d => d.Id == CalBId && d.DisplayName == "Cal B");
    }

    // ── GetConnectionStatus ──────────────────────────────────────────────────

    [Fact]
    public async Task GetConnectionStatus_WhenTokenActive_ReturnsActiveStatus()
    {
        // Arrange
        var (_, tokenStore, currentUser, systemUnderTest) = CreateSutWithAuth();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");
        tokenStore
            .Setup(t => t.GetAuthStatusAsync("u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.Active, null, null));

        // Act
        var result = await systemUnderTest.GetConnectionStatus(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(ok.Value));
        payload["status"].Should().Be("active");
        payload["lastError"].Should().BeNull();
        payload["since"].Should().BeNull();
    }

    [Fact]
    public async Task GetConnectionStatus_WhenTokenNeedsReauth_ReturnsReauthStatusWithDetails()
    {
        // Arrange
        var (_, tokenStore, currentUser, systemUnderTest) = CreateSutWithAuth();
        var since = new DateTimeOffset(2026, 5, 13, 18, 34, 0, TimeSpan.Zero);
        currentUser.SetupGet(c => c.UserId).Returns("u-2");
        tokenStore
            .Setup(t => t.GetAuthStatusAsync("u-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthStatusResult(TokenAuthStatus.NeedsReauth, "Token has been expired or revoked.", since));

        // Act
        var result = await systemUnderTest.GetConnectionStatus(CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(ok.Value));
        payload["status"].Should().Be("needs_reauth");
        payload["lastError"].Should().Be("Token has been expired or revoked.");
        payload["since"].Should().Be(since);
    }

    [Fact]
    public async Task GetConnectionStatus_WhenNoCurrentUser_ReturnsUnauthorized()
    {
        // Arrange
        var (_, _, currentUser, systemUnderTest) = CreateSutWithAuth();
        currentUser.SetupGet(c => c.UserId).Returns((string?)null);

        // Act
        var result = await systemUnderTest.GetConnectionStatus(CancellationToken.None);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    // ── UpdateCalendarSettings ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateCalendarSettings_WithOtherUsersCalendarId_ReturnsNotFound()
    {
        var (calendarRepository, _, currentUser, _, _, systemUnderTest) = CreateFullSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");

        // Scoped fetch returns null — calendar exists but belongs to a different user
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarInfo?)null);

        var request = new CalendarSettingsRequest(IsVisible: true, IsShared: false);

        var result = await systemUnderTest.UpdateCalendarSettings(CalAId, request, CancellationToken.None);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateCalendarSettings_WhenSharedDesignationChanges_EnqueuesReconcileAndWakesWorker()
    {
        // Arrange
        var (calendarRepository, _, currentUser, syncJobQueue, syncJobSignal, systemUnderTest) = CreateFullSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");

        var cal = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsShared = false, IsVisible = true };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cal);

        var request = new CalendarSettingsRequest(IsVisible: true, IsShared: true);

        // Act
        var result = await systemUnderTest.UpdateCalendarSettings(CalAId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        syncJobQueue.Verify(q => q.EnqueueAsync(
            "u-1", null, SyncJobSource.DesignationChange, null, It.IsAny<CancellationToken>()), Times.Once);
        syncJobSignal.Verify(s => s.Release(), Times.Once);
    }

    [Fact]
    public async Task UpdateCalendarSettings_WhenSharedDesignationUnchanged_DoesNotEnqueue()
    {
        // Arrange
        var (calendarRepository, _, currentUser, syncJobQueue, syncJobSignal, systemUnderTest) = CreateFullSut();
        currentUser.SetupGet(c => c.UserId).Returns("u-1");

        var cal = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000", IsShared = false, IsVisible = false };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(CalAId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cal);

        var request = new CalendarSettingsRequest(IsVisible: true, IsShared: false);

        // Act
        var result = await systemUnderTest.UpdateCalendarSettings(CalAId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        syncJobQueue.Verify(q => q.EnqueueAsync(
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        syncJobSignal.Verify(s => s.Release(), Times.Never);
    }

    private static (Mock<ICalendarRepository>, CalendarsController) CreateSut(TimeZoneInfo? displayZone = null)
    {
        var (calRepo, _, _, _, _, sut) = CreateFullSut(displayZone);
        return (calRepo, sut);
    }

    private static (
        Mock<ICalendarRepository> CalendarRepo,
        Mock<ITokenStore> TokenStore,
        Mock<ICurrentUserService> CurrentUser,
        CalendarsController SystemUnderTest) CreateSutWithAuth()
    {
        var (calRepo, tokenStore, currentUser, _, _, sut) = CreateFullSut();
        return (calRepo, tokenStore, currentUser, sut);
    }

    private static (
        Mock<ICalendarRepository> CalendarRepo,
        Mock<ITokenStore> TokenStore,
        Mock<ICurrentUserService> CurrentUser,
        Mock<ICalendarSyncJobQueue> SyncJobQueue,
        Mock<ISyncJobSignal> SyncJobSignal,
        CalendarsController SystemUnderTest) CreateFullSut(TimeZoneInfo? displayZone = null)
    {
        var calendarRepositoryMock = new Mock<ICalendarRepository>();
        var loggerMock = new Mock<ILogger<CalendarsController>>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var currentUserMock = new Mock<ICurrentUserService>();
        var syncJobQueueMock = new Mock<ICalendarSyncJobQueue>();
        var syncJobSignalMock = new Mock<ISyncJobSignal>();
        var timeZoneServiceMock = new Mock<ITimeZoneService>();

        // Configured rather than left to Moq: an unconfigured Task-returning method answers with a
        // null Task, which an await turns into a NullReferenceException rather than a legible failure.
        // UTC keeps every pre-existing test's expectations exactly as they were — the grid used to key
        // cells by the UTC date for every event, so UTC is the zone in which nothing changes.
        timeZoneServiceMock.Setup(s => s.GetDisplayZoneAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(displayZone ?? TimeZoneInfo.Utc);

        var systemUnderTest = new CalendarsController(
            calendarRepositoryMock.Object,
            loggerMock.Object,
            tokenStoreMock.Object,
            currentUserMock.Object,
            syncJobQueueMock.Object,
            syncJobSignalMock.Object,
            timeZoneServiceMock.Object);

        return (calendarRepositoryMock, tokenStoreMock, currentUserMock, syncJobQueueMock, syncJobSignalMock, systemUnderTest);
    }
}
