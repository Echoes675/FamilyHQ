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
    public async Task GetEventsForMonth_InvalidMonth_Returns400()
    {
        // Arrange
        var (_, systemUnderTest) = CreateSut();

        // Act
        var result = await systemUnderTest.GetEventsForMonth(2026, 13, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
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

    private static (Mock<ICalendarRepository>, CalendarsController) CreateSut()
    {
        var calendarRepositoryMock = new Mock<ICalendarRepository>();
        var loggerMock = new Mock<ILogger<CalendarsController>>();

        var systemUnderTest = new CalendarsController(
            calendarRepositoryMock.Object,
            loggerMock.Object);

        return (calendarRepositoryMock, systemUnderTest);
    }
}
