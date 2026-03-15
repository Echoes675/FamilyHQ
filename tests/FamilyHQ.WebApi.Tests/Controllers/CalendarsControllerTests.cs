using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Core.ViewModels;
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

    // ── ReassignEvent ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReassignEvent_WhenValidRequest_Returns200WithViewModel()
    {
        // Arrange
        var (calendarRepository, calendarEventService, systemUnderTest) = CreateSut();

        var toCal = new CalendarInfo { Id = CalBId, DisplayName = "Cal B", Color = "#0000ff" };
        var updatedEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "new-google-id",
            Title = "Moved Event",
            Start = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false,
            Calendars = new List<CalendarInfo> { toCal }
        };

        calendarEventService.Setup(s => s.ReassignAsync(CalAId, EventId, It.IsAny<ReassignEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedEvent);

        var request = new ReassignEventRequest(CalBId, "Moved Event", updatedEvent.Start, updatedEvent.End, false, null, null);

        // Act
        var result = await systemUnderTest.ReassignEvent(CalAId, EventId, request, CancellationToken.None);

        // Assert
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var vm = ok.Value.Should().BeOfType<CalendarEventViewModel>().Subject;
        vm.CalendarId.Should().Be(CalBId);
        vm.LinkedCalendars.Should().ContainSingle(c => c.Id == CalBId);
    }

    [Fact]
    public async Task ReassignEvent_WhenServiceReturnsNull_Returns404()
    {
        // Arrange
        var (calendarRepository, calendarEventService, systemUnderTest) = CreateSut();

        calendarEventService.Setup(s => s.ReassignAsync(CalAId, EventId, It.IsAny<ReassignEventRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var request = new ReassignEventRequest(CalBId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignEvent(CalAId, EventId, request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetEventsForMonth ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetEventsForMonth_EventWithTwoCalendars_ReturnsLinkedCalendarsOnViewModel()
    {
        // Arrange
        var (calendarRepository, calendarEventService, systemUnderTest) = CreateSut();

        var calA = new CalendarInfo { Id = CalAId, DisplayName = "Cal A", Color = "#ff0000" };
        var calB = new CalendarInfo { Id = CalBId, DisplayName = "Cal B", Color = "#0000ff" };

        var evt = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Title = "Shared Event",
            Start = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
            End = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
            IsAllDay = false,
            Calendars = new List<CalendarInfo> { calA, calB }
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
        dayEvents.Should().ContainSingle();
        dayEvents[0].LinkedCalendars.Should().HaveCount(2);
    }

    private static (Mock<ICalendarRepository>, Mock<ICalendarEventService>, CalendarsController) CreateSut()
    {
        var calendarRepositoryMock = new Mock<ICalendarRepository>();
        var googleClientMock = new Mock<IGoogleCalendarClient>();
        var calendarEventServiceMock = new Mock<ICalendarEventService>();
        var loggerMock = new Mock<ILogger<CalendarsController>>();

        var systemUnderTest = new CalendarsController(
            calendarRepositoryMock.Object,
            googleClientMock.Object,
            calendarEventServiceMock.Object,
            loggerMock.Object);

        return (calendarRepositoryMock, calendarEventServiceMock, systemUnderTest);
    }
}
