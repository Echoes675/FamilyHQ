using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
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
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId      = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var googleCalendarId = "test@group.calendar.google.com";
        var startDate       = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate         = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);

        var calendarInfo = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendarInfo });

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-1", Title = "Event 1" },
            new CalendarEvent { GoogleEventId = "evt-2", Title = "Event 2" }
        };

        client.Setup(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "new_sync_token"));

        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

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
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId       = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var googleCalendarId = "test@group.calendar.google.com";

        var calendarInfo      = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        var existingSyncState = new SyncState { CalendarInfoId = calendarId, SyncToken = "old_token" };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendarInfo);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSyncState);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendarInfo });

        var mockEvents = new List<CalendarEvent>
        {
            new CalendarEvent { GoogleEventId = "evt-inc", Title = "Incremental Event" }
        };

        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "old_token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((mockEvents, "next_token"));
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

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
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var startDate        = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate          = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);
        var calendarId       = Guid.Parse("33333333-3333-3333-3333-333333333333");
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
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
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
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var startDate        = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var endDate          = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(30);
        var calendarId       = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var googleCalendarId = "test@group.calendar.google.com";

        var googleCalendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests" };
        var localCalendar  = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Tests Local" };

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
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert
        calendarRepository.Verify(r => r.AddCalendarAsync(It.IsAny<CalendarInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAllAsync_WhenMultipleCalendarsAndNoShared_AutoDesignatesFirstAsShared()
    {
        // Arrange
        var (client, calendarRepository, _, systemUnderTest) = CreateSut();
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate   = startDate.AddDays(30);

        var workCal = new CalendarInfo
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            GoogleCalendarId = "work@group.calendar.google.com",
            DisplayName = "Work",
            IsShared = false
        };
        var personalCal = new CalendarInfo
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            GoogleCalendarId = "personal@group.calendar.google.com",
            DisplayName = "Personal",
            IsShared = false
        };

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { workCal, personalCal });

        // Simulate repository state: both calendars already persisted (returned both passes)
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { workCal, personalCal });
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                id == workCal.Id ? workCal : personalCal);
        calendarRepository.Setup(r => r.GetSyncStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(It.IsAny<Guid>(), startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert — the first calendar in the Google list is designated shared
        workCal.IsShared.Should().BeTrue("the first calendar should become shared when no prior designation exists");
        personalCal.IsShared.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAllAsync_WhenSingleCalendarAndNoShared_DoesNotDesignateShared()
    {
        // Arrange
        var (client, calendarRepository, _, systemUnderTest) = CreateSut();
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate   = startDate.AddDays(30);

        var soloCal = new CalendarInfo
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            GoogleCalendarId = "solo@group.calendar.google.com",
            DisplayName = "Solo",
            IsShared = false
        };

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { soloCal });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { soloCal });
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(soloCal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(soloCal);
        calendarRepository.Setup(r => r.GetSyncStateAsync(soloCal.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(soloCal.GoogleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(soloCal.Id, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert — single-calendar account has no shared concept
        soloCal.IsShared.Should().BeFalse("a user with only one calendar should not have a shared calendar designation");
    }

    [Fact]
    public async Task SyncAllAsync_WhenSharedAlreadyDesignated_DoesNotOverwrite()
    {
        // Arrange
        var (client, calendarRepository, _, systemUnderTest) = CreateSut();
        var startDate = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate   = startDate.AddDays(30);

        var workCal = new CalendarInfo
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            GoogleCalendarId = "work@group.calendar.google.com",
            DisplayName = "Work",
            IsShared = false
        };
        var familyCal = new CalendarInfo
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            GoogleCalendarId = "family@group.calendar.google.com",
            DisplayName = "Family",
            IsShared = true // already designated
        };

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { workCal, familyCal });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { workCal, familyCal });
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) =>
                id == workCal.Id ? workCal : familyCal);
        calendarRepository.Setup(r => r.GetSyncStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(It.IsAny<Guid>(), startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        // Act
        await systemUnderTest.SyncAllAsync(startDate, endDate);

        // Assert — existing shared designation is preserved
        workCal.IsShared.Should().BeFalse("the non-shared calendar should remain non-shared");
        familyCal.IsShared.Should().BeTrue("the already-designated shared calendar should be preserved");
    }

    // ── Member-tag sync tests ─────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_NewEvent_SetsOwnerCalendarInfoIdAndMembers()
    {
        // Arrange
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId       = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var googleCalendarId = "owner@group.calendar.google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var end              = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Owner Cal" };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });

        var newEvent = new CalendarEvent { GoogleEventId = "evt-1", Title = "New Event" };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { newEvent }, "sync-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.OwnerCalendarInfoId == calendarId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_ExistingEvent_UpdatesProperties()
    {
        // Arrange
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId       = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var googleCalendarId = "cal@google.com";
        var start            = DateTimeOffset.UtcNow.AddDays(-1);
        var end              = DateTimeOffset.UtcNow.AddDays(1);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Cal" };
        var existing = new CalendarEvent
        {
            Id            = Guid.NewGuid(),
            GoogleEventId = "evt-existing",
            Title         = "Old Title",
            Members       = new List<CalendarInfo> { calendar }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });

        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-existing", Title = "New Title" } }, "token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        calendarRepository.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_TombstoneEvent_DeletesFromDb()
    {
        // Arrange
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId       = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var googleCalendarId = "cal-a@google.com";
        var start            = DateTimeOffset.UtcNow.AddDays(-1);
        var end              = DateTimeOffset.UtcNow.AddDays(1);
        var eventId          = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Cal A" };
        var trackedEvent = new CalendarEvent
        {
            Id            = eventId,
            GoogleEventId = "evt-orphan",
            Title         = "Only On Cal A",
            Members       = new List<CalendarInfo> { calendar }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = calendarId, SyncToken = "incremental-token" });
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });
        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "incremental-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-orphan", Title = "CANCELLED_TOMBSTONE" } }, "new-token"));
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedEvent);

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        calendarRepository.Verify(r => r.DeleteEventAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_FullSync_RemovesObsoleteEvents()
    {
        // Arrange
        var (client, calendarRepository, tagParser, systemUnderTest) = CreateSut();
        var calendarId       = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var googleCalendarId = "cal@google.com";
        var start            = DateTimeOffset.UtcNow.AddDays(-1);
        var end              = DateTimeOffset.UtcNow.AddDays(1);
        var obsoleteEventId  = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Cal" };
        var obsoleteEvent = new CalendarEvent
        {
            Id            = obsoleteEventId,
            GoogleEventId = "evt-gone",
            Title         = "Gone Event"
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null); // Full sync
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token")); // Empty result from Google
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { obsoleteEvent }); // Local has a stale event

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert — obsolete event removed
        calendarRepository.Verify(r => r.DeleteEventAsync(obsoleteEventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, CalendarSyncService sut) CreateSut()
    {
        var clientMock     = new Mock<IGoogleCalendarClient>();
        var repoMock       = new Mock<ICalendarRepository>();
        var tagParserMock  = new Mock<IMemberTagParser>();
        var loggerMock     = new Mock<ILogger<CalendarSyncService>>();

        // Default tag parser returns empty list
        tagParserMock.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new List<string>());

        var sut = new CalendarSyncService(
            clientMock.Object,
            repoMock.Object,
            tagParserMock.Object,
            loggerMock.Object);

        return (clientMock, repoMock, tagParserMock, sut);
    }
}
