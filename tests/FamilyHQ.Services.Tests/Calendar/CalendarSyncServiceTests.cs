using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarSyncServiceTests
{
    [Fact]
    public async Task SyncAsync_WhenNoExistingToken_PerformsFullSyncWithWindows()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calendarId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var googleCalendarId = "test@group.calendar.google.com";
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);

        var calendarInfo = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);

        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null); // No token exists!

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-1", Title = "Event 1" },
            new CalendarEvent { GoogleEventId = "evt-2", Title = "Event 2" }
        };

        client.Setup(c => c.GetEventsAsync(
                googleCalendarId, 
                startDate, 
                endDate, 
                null, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "new_sync_token"));

        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAsync(calendarId, startDate, endDate);

        // Assert
        client.Verify(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        calendarRepository.Verify(r => r.AddSyncStateAsync(It.Is<SyncState>(s => 
            s.SyncToken == "new_sync_token" && 
            s.SyncWindowStart == startDate && 
            s.SyncWindowEnd == endDate), It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenTokenExists_PerformsIncrementalSync()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calendarId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var googleCalendarId = "test@group.calendar.google.com";

        var calendarInfo = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        var existingSyncState = new SyncState { CalendarInfoId = calendarId, SyncToken = "old_token" };
        
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);

        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSyncState);

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-inc", Title = "Incremental Event" }
        };

        client.Setup(c => c.GetEventsAsync(
                googleCalendarId, 
                null, 
                null, 
                "old_token", 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "next_token"));

        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        await systemUnderTest.SyncAsync(calendarId, now, now);

        // Assert
        client.Verify(c => c.GetEventsAsync(googleCalendarId, null, null, "old_token", It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.SaveSyncStateAsync(It.Is<SyncState>(s => 
            s.SyncToken == "next_token"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAllAsync_WithNewCalendar_AddsCalendarAndSyncsEvents()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);
        var calendarId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var googleCalendarId = "test@group.calendar.google.com";

        var googleCalendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        
        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { googleCalendar });

        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo>()); // No local calendars

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(googleCalendar);

        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        client.Setup(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));

        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert
        calendarRepository.Verify(r => r.AddCalendarAsync(googleCalendar, It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        client.Verify(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAllAsync_WithExistingCalendar_SyncsEvents()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);
        var calendarId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var googleCalendarId = "test@group.calendar.google.com";

        var googleCalendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        var localCalendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests Local" };
        
        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { googleCalendar });

        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { localCalendar });

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localCalendar);

        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        client.Setup(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));

        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert
        calendarRepository.Verify(r => r.AddCalendarAsync(It.IsAny<CalendarInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<IGoogleCalendarClient> Client, Mock<ICalendarRepository> calendarRepository, CalendarSyncService systemUnderTest) CreateSut()
    {
        var clientMock = new Mock<IGoogleCalendarClient>();
        var calendarRepositoryMock = new Mock<ICalendarRepository>();
        var loggerMock = new Mock<ILogger<CalendarSyncService>>();

        var systemUnderTest = new CalendarSyncService(
            clientMock.Object,
            calendarRepositoryMock.Object,
            loggerMock.Object);

        return (clientMock, calendarRepositoryMock, systemUnderTest);
    }
}
