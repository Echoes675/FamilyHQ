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

    // ── Many-to-Many relationship tests ───────────────────────────────────────

    [Fact]
    public async Task SyncAsync_WhenEventExistsOnDifferentCalendar_LinksToSecondCalendar()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var calBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var calA = new CalendarInfo { Id = calAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
        var calB = new CalendarInfo { Id = calBId, GoogleCalendarId = "cal-b@google.com", DisplayName = "Cal B" };
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);

        var existingEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "evt-shared",
            Title = "Shared Event",
            Calendars = new List<CalendarInfo> { calA }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calB);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(calB.GoogleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-shared", Title = "Shared Event" } }, "token-b"));
        calendarRepository.Setup(r => r.GetEventsAsync(calBId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>()); // Not yet linked to calB
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-shared", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent); // Exists on calA
        // Mock GetEventByIdAsync to return a tracked instance with the updated calendars
        calendarRepository.Setup(r => r.GetEventByIdAsync(existingEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        // Act
        await systemUnderTest.SyncAsync(calBId, start, end);

        // Assert — event updated with calB added; not inserted again
        calendarRepository.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => e.Calendars.Any(c => c.Id == calBId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenEventAlreadyLinkedToThisCalendar_UpdatesPropertiesOnly()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var calA = new CalendarInfo { Id = calAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);

        var linkedEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "evt-existing",
            Title = "Old Title",
            Calendars = new List<CalendarInfo> { calA }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(calA.GoogleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-existing", Title = "New Title" } }, "token"));
        calendarRepository.Setup(r => r.GetEventsAsync(calAId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent> { linkedEvent }); // Already linked

        // Act
        await systemUnderTest.SyncAsync(calAId, start, end);

        // Assert — properties updated; cross-calendar lookup never needed
        calendarRepository.Verify(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => e.Title == "New Title"),
            It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenTombstoneAndEventHasOtherCalendars_UnlinksButDoesNotDelete()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var calBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var calA = new CalendarInfo { Id = calAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
        var calB = new CalendarInfo { Id = calBId, GoogleCalendarId = "cal-b@google.com", DisplayName = "Cal B" };
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);

        var eventLinkedToBoth = new CalendarEvent
        {
            Id = Guid.NewGuid(),
            GoogleEventId = "evt-tombstone",
            Title = "Event on Two Calendars",
            Calendars = new List<CalendarInfo> { calA, calB }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = calAId, SyncToken = "incremental-token" });
        client.Setup(c => c.GetEventsAsync(calA.GoogleCalendarId, null, null, "incremental-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-tombstone", Title = "CANCELLED_TOMBSTONE" } }, "new-token"));
        calendarRepository.Setup(r => r.GetEventsAsync(calAId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-tombstone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventLinkedToBoth);

        // Act
        await systemUnderTest.SyncAsync(calAId, start, end);

        // Assert — calA removed from Calendars but event not deleted (still linked to calB)
        calendarRepository.Verify(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => !e.Calendars.Any(c => c.Id == calAId) && e.Calendars.Any(c => c.Id == calBId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenTombstoneAndEventHasNoOtherCalendars_DeletesEvent()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var calA = new CalendarInfo { Id = calAId, GoogleCalendarId = "cal-a@google.com", DisplayName = "Cal A" };
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);
        var eventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var eventOnlyOnCalA = new CalendarEvent
        {
            Id = eventId,
            GoogleEventId = "evt-orphan",
            Title = "Only On Cal A",
            Calendars = new List<CalendarInfo> { calA }
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calA);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = calAId, SyncToken = "incremental-token" });
        client.Setup(c => c.GetEventsAsync(calA.GoogleCalendarId, null, null, "incremental-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-orphan", Title = "CANCELLED_TOMBSTONE" } }, "new-token"));
        calendarRepository.Setup(r => r.GetEventsAsync(calAId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync(eventOnlyOnCalA);

        // Act
        await systemUnderTest.SyncAsync(calAId, start, end);

        // Assert — event deleted entirely as it has no remaining calendar links
        calendarRepository.Verify(r => r.DeleteEventAsync(eventId, It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── OwnerCalendarInfoId + IsExternallyOwned tests ────────────────────────

    [Fact]
    public async Task SyncAsync_FirstSync_SetsOwnerCalendarInfoId()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calendarId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var googleCalendarId = "owner@group.calendar.google.com";
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Owner Cal" };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        var newEvent = new CalendarEvent { GoogleEventId = "evt-1", Title = "New Event" };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { newEvent }, "sync-token"));
        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
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
    public async Task SyncAsync_OrganizerSelfFalse_SetsIsExternallyOwned()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var calendarId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var googleCalendarId = "attendee@group.calendar.google.com";
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Attendee Cal" };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        // IsExternallyOwned = true simulates organizer.Self == false
        var externalEvent = new CalendarEvent { GoogleEventId = "evt-external", Title = "External Event", IsExternallyOwned = true };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { externalEvent }, "sync-token"));
        calendarRepository.Setup(r => r.GetEventsAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-external", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.IsExternallyOwned == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_SecondCalendarAttach_DoesNotOverwriteOwnerCalendarInfoId()
    {
        // Arrange
        var (client, calendarRepository, systemUnderTest) = CreateSut();
        var secondCalendarId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var existingOwnerCalendarId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var googleCalendarId = "second@group.calendar.google.com";
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(-1);
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(1);

        var secondCalendar = new CalendarInfo { Id = secondCalendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Second Cal" };
        var existingEvent = new CalendarEvent
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            GoogleEventId = "evt-1",
            Title = "Shared Event",
            OwnerCalendarInfoId = existingOwnerCalendarId,
            Calendars = new List<CalendarInfo>()
        };

        calendarRepository.Setup(r => r.GetCalendarByIdAsync(secondCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondCalendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(secondCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);

        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-1", Title = "Shared Event" } }, "sync-token"));
        calendarRepository.Setup(r => r.GetEventsAsync(secondCalendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>()); // Not yet linked to secondCalendar
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent); // Exists already (owned by existingOwnerCalendarId)
        calendarRepository.Setup(r => r.GetEventByIdAsync(existingEvent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingEvent);

        // Act
        await systemUnderTest.SyncAsync(secondCalendarId, start, end);

        // Assert — second-calendar attach: no new insert, SaveChanges IS called, OwnerCalendarInfoId not overwritten
        calendarRepository.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        existingEvent.OwnerCalendarInfoId.Should().Be(existingOwnerCalendarId);
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
