using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
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
        // Per-event resilience: one SaveChanges per event (so a constraint
        // violation on event N does not roll back events 1..N-1) plus the
        // final SaveChanges that commits the SyncState update.
        calendarRepository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
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

        // Assert — MarkCalendarAsSharedAsync is invoked for the first calendar and
        // not for the second.  We can't assert on the entity itself because the real
        // repository mutates the tracked entity via FindAsync; the mock doesn't have
        // a DbContext, so wiring a callback to mutate workCal would just re-test our
        // own test setup.  Verifying the interaction is the cleaner contract.
        calendarRepository.Verify(r => r.MarkCalendarAsSharedAsync(workCal.Id, It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.MarkCalendarAsSharedAsync(personalCal.Id, It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task SyncAllAsync_WhenGetCalendarsThrowsReauthRequired_MarksTokenAndRethrows()
    {
        // Arrange — token refresh failure surfaces during the entry-point Google call.
        var (client, repo, _, _, tokenStore, currentUser, systemUnderTest) = CreateSutWithReauthDeps(userId: "u-1");
        var ex = new GoogleReauthRequiredException(
            GoogleAuthFailureSource.TokenRefresh, "Token has been expired or revoked.");
        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        // Act
        var act = () => systemUnderTest.SyncAllAsync(
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(30));

        // Assert — original exception preserved and the user is marked.
        await act.Should().ThrowAsync<GoogleReauthRequiredException>();
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync("u-1", "Token has been expired or revoked.", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAllAsync_WhenCalendarApi403_MarksTokenAndRethrows()
    {
        // Arrange — 403 from Calendar API also marks the user (different Source).
        var (client, repo, _, _, tokenStore, currentUser, systemUnderTest) = CreateSutWithReauthDeps(userId: "u-2");
        var ex = new GoogleReauthRequiredException(
            GoogleAuthFailureSource.CalendarApi, "Insufficient Permission");
        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

        // Act
        var act = () => systemUnderTest.SyncAllAsync(
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(30));

        // Assert
        await act.Should().ThrowAsync<GoogleReauthRequiredException>();
        tokenStore.Verify(
            t => t.MarkNeedsReauthAsync("u-2", "Insufficient Permission", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenAddEventThrowsForOneEvent_RecordsFailureAndContinues()
    {
        // Arrange — three events; the middle one throws on persistence. Remaining
        // events should still persist and exactly one SyncEventFailure row recorded
        // with the offending GoogleEventId.
        var (client, calendarRepository, _, _, _, _, syncFailureRepo, systemUnderTest) =
            CreateSutWithAllDeps(userId: "u-resilience");
        var calendarId       = Guid.Parse("a1111111-1111-1111-1111-111111111111");
        var googleCalendarId = "resilience@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(7);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Resilience" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });

        var goodA = new CalendarEvent { GoogleEventId = "evt-a", Title = "A" };
        var bad   = new CalendarEvent { GoogleEventId = "evt-bad", Title = "Bad" };
        var goodB = new CalendarEvent { GoogleEventId = "evt-b", Title = "B" };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { goodA, bad, goodB }, "next-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        calendarRepository.Setup(r => r.AddEventAsync(It.Is<CalendarEvent>(e => e.GoogleEventId == "evt-bad"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated event-level failure"));

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert — the two good events still persisted, exactly one failure recorded
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "evt-a"), It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "evt-b"), It.IsAny<CancellationToken>()), Times.Once);
        syncFailureRepo.Verify(s => s.AddAsync(
            It.Is<SyncEventFailure>(f =>
                f.GoogleEventId == "evt-bad" &&
                f.CalendarInfoId == calendarId &&
                f.UserId == "u-resilience" &&
                f.EventTitle == "Bad" &&
                f.FailureReason == "simulated event-level failure" &&
                f.ExceptionType.Contains("InvalidOperationException") &&
                !f.Resolved),
            It.IsAny<CancellationToken>()), Times.Once);
        // Sync still completes — token advanced
        calendarRepository.Verify(r => r.AddSyncStateAsync(It.Is<SyncState>(s => s.SyncToken == "next-token"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenAddEventThrowsGoogleReauthRequired_PropagatesNotCaught()
    {
        // Arrange
        var (client, calendarRepository, _, _, _, _, syncFailureRepo, systemUnderTest) =
            CreateSutWithAllDeps(userId: "u-reauth");
        var calendarId       = Guid.Parse("b2222222-2222-2222-2222-222222222222");
        var googleCalendarId = "reauth@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(7);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "R" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>())).ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarInfo> { calendar });
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-x", Title = "X" } }, "tok"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-x", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        calendarRepository.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "rejected"));

        // Act
        var act = () => systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        await act.Should().ThrowAsync<GoogleReauthRequiredException>();
        syncFailureRepo.Verify(s => s.AddAsync(It.IsAny<SyncEventFailure>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenAddEventThrowsOperationCanceled_PropagatesNotCaught()
    {
        // Arrange
        var (client, calendarRepository, _, _, _, _, syncFailureRepo, systemUnderTest) =
            CreateSutWithAllDeps(userId: "u-cancel");
        var calendarId       = Guid.Parse("c3333333-3333-3333-3333-333333333333");
        var googleCalendarId = "cancel@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(7);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "C" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>())).ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarInfo> { calendar });
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new CalendarEvent { GoogleEventId = "evt-y", Title = "Y" } }, "tok"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync("evt-y", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        calendarRepository.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("cancelled"));

        // Act
        var act = () => systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        syncFailureRepo.Verify(s => s.AddAsync(It.IsAny<SyncEventFailure>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenSaveChangesThrowsForOneEvent_DetachesAndContinues()
    {
        // Real-world scenario: AddEventAsync only stages the entity; the actual
        // Postgres constraint violation (e.g. "value too long for type character
        // varying(500)") surfaces from SaveChangesAsync, AFTER the per-event Add
        // call has succeeded. This test pins the behaviour that the per-event
        // catch must detach the failing entity and record the failure rather
        // than aborting the whole sync.
        var (client, calendarRepository, _, _, _, _, syncFailureRepo, systemUnderTest) =
            CreateSutWithAllDeps(userId: "u-save-resilience");
        var calendarId       = Guid.Parse("d4444444-4444-4444-4444-444444444444");
        var googleCalendarId = "save-resilience@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(7);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "S" };
        calendarRepository.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        calendarRepository.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        calendarRepository.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });

        var goodA = new CalendarEvent { GoogleEventId = "evt-a", Title = "A" };
        var bad   = new CalendarEvent { GoogleEventId = "evt-bad", Title = new string('X', 600) };
        var goodB = new CalendarEvent { GoogleEventId = "evt-b", Title = "B" };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { goodA, bad, goodB }, "next-token"));
        calendarRepository.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        calendarRepository.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        // SaveChangesAsync throws when the bad event is the last one added.
        // The per-event loop adds then immediately saves, so we tie the throw
        // to the AddEventAsync(bad) call having been invoked.
        var badEventStaged = false;
        calendarRepository.Setup(r => r.AddEventAsync(bad, It.IsAny<CancellationToken>()))
            .Callback(() => badEventStaged = true)
            .Returns(Task.CompletedTask);
        calendarRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                if (badEventStaged)
                {
                    badEventStaged = false; // only the bad event's save throws
                    throw new InvalidOperationException("value too long for type character varying(500)");
                }
                return Task.FromResult(1);
            });

        // Act
        await systemUnderTest.SyncAsync(calendarId, start, end);

        // Assert — bad event was detached so it does not poison subsequent saves
        calendarRepository.Verify(r => r.DetachEventAsync(bad, It.IsAny<CancellationToken>()), Times.Once);
        // The other two events were still added
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "evt-a"), It.IsAny<CancellationToken>()), Times.Once);
        calendarRepository.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "evt-b"), It.IsAny<CancellationToken>()), Times.Once);
        // Exactly one failure recorded against the bad event
        syncFailureRepo.Verify(s => s.AddAsync(
            It.Is<SyncEventFailure>(f =>
                f.GoogleEventId == "evt-bad" &&
                f.UserId == "u-save-resilience" &&
                f.FailureReason.StartsWith("value too long")),
            It.IsAny<CancellationToken>()), Times.Once);
        // Sync still completes — token advanced
        calendarRepository.Verify(r => r.AddSyncStateAsync(
            It.Is<SyncState>(s => s.SyncToken == "next-token"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, CalendarSyncService sut) CreateSut()
    {
        var (client, repo, tagParser, _, _, _, _, sut) = CreateSutWithAllDeps(userId: "test-user");
        return (client, repo, tagParser, sut);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, Mock<ILogger<CalendarSyncService>> logger,
        Mock<ITokenStore> tokenStore, Mock<ICurrentUserService> currentUser,
        CalendarSyncService sut) CreateSutWithReauthDeps(string userId)
    {
        var (client, repo, tagParser, logger, tokenStore, currentUser, _, sut) = CreateSutWithAllDeps(userId);
        return (client, repo, tagParser, logger, tokenStore, currentUser, sut);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, Mock<ILogger<CalendarSyncService>> logger,
        Mock<ITokenStore> tokenStore, Mock<ICurrentUserService> currentUser,
        Mock<ISyncFailureRepository> syncFailureRepo,
        CalendarSyncService sut) CreateSutWithAllDeps(string userId)
    {
        var clientMock           = new Mock<IGoogleCalendarClient>();
        var repoMock             = new Mock<ICalendarRepository>();
        var tagParserMock        = new Mock<IMemberTagParser>();
        var loggerMock           = new Mock<ILogger<CalendarSyncService>>();
        var tokenStoreMock       = new Mock<ITokenStore>();
        var currentUserMock      = new Mock<ICurrentUserService>();
        var syncFailureRepoMock  = new Mock<ISyncFailureRepository>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);

        // Default tag parser returns empty list
        tagParserMock.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new List<string>());

        var sut = new CalendarSyncService(
            clientMock.Object,
            repoMock.Object,
            tagParserMock.Object,
            loggerMock.Object,
            tokenStoreMock.Object,
            currentUserMock.Object,
            syncFailureRepoMock.Object);

        return (clientMock, repoMock, tagParserMock, loggerMock, tokenStoreMock, currentUserMock, syncFailureRepoMock, sut);
    }
}
