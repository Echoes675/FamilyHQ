using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class CalendarSyncServiceTests
{
    private readonly Mock<IGoogleCalendarClient> _googleCalendarClientMock;
    private readonly Mock<ICalendarRepository> _calendarRepositoryMock;
    private readonly Mock<ILogger<CalendarSyncService>> _loggerMock;
    private readonly CalendarSyncService _sut;

    public CalendarSyncServiceTests()
    {
        _googleCalendarClientMock = new Mock<IGoogleCalendarClient>();
        _calendarRepositoryMock = new Mock<ICalendarRepository>();
        _loggerMock = new Mock<ILogger<CalendarSyncService>>();

        _sut = new CalendarSyncService(
            _googleCalendarClientMock.Object,
            _calendarRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task SyncAsync_WhenNoExistingToken_PerformsFullSyncWithWindows()
    {
        // Arrange
        var calendarId = Guid.NewGuid();
        var googleCalendarId = "test@group.calendar.google.com";
        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(30);

        var calendarInfo = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        _calendarRepositoryMock
            .Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);

        _calendarRepositoryMock
            .Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null); // No token exists!

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-1", Title = "Event 1" },
            new CalendarEvent { GoogleEventId = "evt-2", Title = "Event 2" }
        };

        _googleCalendarClientMock
            .Setup(client => client.GetEventsAsync(
                googleCalendarId, 
                startDate, 
                endDate, 
                null, 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "new_sync_token"));

        // Act
        await _sut.SyncAsync(calendarId, startDate, endDate);

        // Assert
        // Verified we called it with time windows and NOT a sync token
        _googleCalendarClientMock.Verify(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()), Times.Once);
        
        // Verified both events were added
        _calendarRepositoryMock.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        
        // Verified Sync State was saved with the windows + token
        _calendarRepositoryMock.Verify(r => r.SaveSyncStateAsync(It.Is<SyncState>(s => 
            s.SyncToken == "new_sync_token" && 
            s.SyncWindowStart == startDate && 
            s.SyncWindowEnd == endDate), It.IsAny<CancellationToken>()), Times.Once);
            
        // Verified save changes was called
        _calendarRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenTokenExists_PerformsIncrementalSync()
    {
        // Arrange
        var calendarId = Guid.NewGuid();
        var googleCalendarId = "test@group.calendar.google.com";

        var calendarInfo = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        var existingSyncState = new SyncState { CalendarInfoId = calendarId, SyncToken = "old_token" };
        
        _calendarRepositoryMock
            .Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);

        _calendarRepositoryMock
            .Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSyncState);

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-inc", Title = "Incremental Event" }
        };

        _googleCalendarClientMock
            .Setup(client => client.GetEventsAsync(
                googleCalendarId, 
                null, 
                null, 
                "old_token", 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "next_token"));

        // Act
        await _sut.SyncAsync(calendarId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Assert
        // Verified we called it WITH sync token and WITHOUT windows
        _googleCalendarClientMock.Verify(c => c.GetEventsAsync(googleCalendarId, null, null, "old_token", It.IsAny<CancellationToken>()), Times.Once);
        
        // Verified Save changes with the new incremental token
        _calendarRepositoryMock.Verify(r => r.SaveSyncStateAsync(It.Is<SyncState>(s => 
            s.SyncToken == "next_token"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
