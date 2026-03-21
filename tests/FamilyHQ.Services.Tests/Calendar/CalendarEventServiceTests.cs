using FluentAssertions;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarEventServiceTests
{
    private static readonly Guid FromCalId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ToCalId   = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId   = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task ReassignAsync_WhenValidRequest_MovesEventAndUpdatesFields()
    {
        // Arrange
        var (googleClient, calendarRepository, systemUnderTest) = CreateSut();

        var fromCal = new CalendarInfo { Id = FromCalId, GoogleCalendarId = "from-cal@google.com", DisplayName = "From Cal" };
        var toCal   = new CalendarInfo { Id = ToCalId,   GoogleCalendarId = "to-cal@google.com",   DisplayName = "To Cal"   };

        var existingEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "original-google-id",
            Title = "Old Title",
            Start = DateTimeOffset.UtcNow,
            End = DateTimeOffset.UtcNow.AddHours(1),
            Calendars = new List<CalendarInfo> { fromCal }
        };

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { fromCal, toCal });
        googleClient.Setup(c => c.MoveEventAsync(fromCal.GoogleCalendarId, "original-google-id", toCal.GoogleCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("original-google-id");
        googleClient.Setup(c => c.UpdateEventAsync(toCal.GoogleCalendarId, It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, CancellationToken _) => e);

        var request = new ReassignEventRequest(ToCalId, "New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert — move and update are called; event ID is unchanged
        googleClient.Verify(c => c.MoveEventAsync(fromCal.GoogleCalendarId, "original-google-id", toCal.GoogleCalendarId, It.IsAny<CancellationToken>()), Times.Once);
        googleClient.Verify(c => c.UpdateEventAsync(toCal.GoogleCalendarId, It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        googleClient.Verify(c => c.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        googleClient.Verify(c => c.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        result.Should().NotBeNull();
        result!.GoogleEventId.Should().Be("original-google-id");
        result.Calendars.Should().ContainSingle(c => c.Id == ToCalId);
        result.Calendars.Should().NotContain(c => c.Id == FromCalId);
    }

    [Fact]
    public async Task ReassignAsync_WhenEventNotFound_ReturnsNull()
    {
        // Arrange
        var (googleClient, calendarRepository, systemUnderTest) = CreateSut();

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var request = new ReassignEventRequest(ToCalId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert
        result.Should().BeNull();
        googleClient.Verify(c => c.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignAsync_WhenEventNotLinkedToFromCalendar_ReturnsNull()
    {
        // Arrange
        var (googleClient, calendarRepository, systemUnderTest) = CreateSut();

        var unrelatedCal = new CalendarInfo { Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), GoogleCalendarId = "other@google.com" };
        var existingEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Calendars = new List<CalendarInfo> { unrelatedCal } // not linked to FromCalId
        };

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        var request = new ReassignEventRequest(ToCalId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert
        result.Should().BeNull();
        googleClient.Verify(c => c.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignAsync_WhenToCalendarNotFound_ReturnsNull()
    {
        // Arrange
        var (googleClient, calendarRepository, systemUnderTest) = CreateSut();

        var fromCal = new CalendarInfo { Id = FromCalId, GoogleCalendarId = "from-cal@google.com" };
        var existingEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Calendars = new List<CalendarInfo> { fromCal }
        };

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { fromCal }); // ToCalId not in list

        var request = new ReassignEventRequest(ToCalId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert
        result.Should().BeNull();
        googleClient.Verify(c => c.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReassignAsync_WhenMoveThrows_PropagatesException()
    {
        // Arrange
        var (googleClient, calendarRepository, systemUnderTest) = CreateSut();

        var fromCal = new CalendarInfo { Id = FromCalId, GoogleCalendarId = "from-cal@google.com" };
        var toCal   = new CalendarInfo { Id = ToCalId,   GoogleCalendarId = "to-cal@google.com"   };
        var existingEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "google-id",
            Calendars = new List<CalendarInfo> { fromCal }
        };

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { fromCal, toCal });
        googleClient.Setup(c => c.MoveEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Google API unavailable"));

        var request = new ReassignEventRequest(ToCalId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act & Assert
        await systemUnderTest.Invoking(s => s.ReassignAsync(FromCalId, EventId, request))
            .Should().ThrowAsync<HttpRequestException>();
    }

    private static (Mock<IGoogleCalendarClient>, Mock<ICalendarRepository>, CalendarEventService) CreateSut()
    {
        var googleClientMock = new Mock<IGoogleCalendarClient>();
        var calendarRepositoryMock = new Mock<ICalendarRepository>();
        var loggerMock = new Mock<ILogger<CalendarEventService>>();

        var systemUnderTest = new CalendarEventService(
            googleClientMock.Object,
            calendarRepositoryMock.Object,
            loggerMock.Object);

        return (googleClientMock, calendarRepositoryMock, systemUnderTest);
    }
}
