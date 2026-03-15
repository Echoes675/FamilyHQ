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
    public async Task ReassignAsync_WhenValidRequest_DeletesFromSourceCreatesInTarget()
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

        var createdGoogleEvent = new CalendarEvent
        {
            Id = EventId,
            GoogleEventId = "new-google-id",
            Title = "New Title"
        };

        calendarRepository.Setup(r => r.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { fromCal, toCal });
        googleClient.Setup(c => c.DeleteEventAsync(fromCal.GoogleCalendarId, existingEvent.GoogleEventId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        googleClient.Setup(c => c.CreateEventAsync(toCal.GoogleCalendarId, It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdGoogleEvent);

        var request = new ReassignEventRequest(ToCalId, "New Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var result = await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert — Google ops are the key behaviors
        googleClient.Verify(c => c.DeleteEventAsync(fromCal.GoogleCalendarId, "original-google-id", It.IsAny<CancellationToken>()), Times.Once);
        googleClient.Verify(c => c.CreateEventAsync(toCal.GoogleCalendarId, It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Once);

        // State: returned event reflects new google ID and correct calendar link
        result.Should().NotBeNull();
        result!.GoogleEventId.Should().Be("new-google-id");
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
    public async Task ReassignAsync_WhenGoogleDeleteThrows_PropagatesException()
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
        
        // Mock CreateEventAsync to return a valid event for rollback
        googleClient.Setup(c => c.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEvent { GoogleEventId = "new-google-id" });
        googleClient.Setup(c => c.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Google API unavailable"));

        var request = new ReassignEventRequest(ToCalId, "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null);

        // Act
        var act = async () => await systemUnderTest.ReassignAsync(FromCalId, EventId, request);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
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
