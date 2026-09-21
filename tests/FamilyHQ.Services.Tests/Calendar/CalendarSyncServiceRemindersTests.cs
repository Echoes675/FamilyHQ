using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

// FHQ-189: the sync half — reminders reach existing rows, the one-off backfill runs exactly once,
// and a phone's reminder-only edit is not mistaken for the kiosk's own echo.
public class CalendarSyncServiceRemindersTests
{
    private static readonly Guid CalendarId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const string GoogleCalendarId = "reminders@group.calendar.google.com";

    [Fact]
    public async Task SyncedReminders_AreCopiedOntoAnExistingRow()
    {
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist", Reminders = null };
        var fetched  = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.Explicit([new("popup", 30)]) };

        var repo = await SyncOneEventAsync(existing, fetched);

        repo.Saved.Single().Reminders!.Overrides.Should().ContainSingle().Which.Minutes.Should().Be(30);
    }

    [Fact]
    public async Task RemindersRemovedInGoogle_AreRemovedLocally()
    {
        // Unlike IanaTimeZone — which is only ever filled in, never cleared — reminders are
        // authoritative on every fetch: the user really can remove the last one.
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.Explicit([new("popup", 30)]) };
        var fetched  = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.ExplicitlyNone };

        var repo = await SyncOneEventAsync(existing, fetched);

        repo.Saved.Single().Reminders!.SameAs(EventReminders.ExplicitlyNone).Should().BeTrue();
    }

    [Fact]
    public async Task AFetchWithNoRemindersObject_LeavesTheStoredValueAlone()
    {
        // Null from the client means "Google said nothing about reminders", not "none".
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.Explicit([new("popup", 30)]) };
        var fetched  = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist", Reminders = null };

        var repo = await SyncOneEventAsync(existing, fetched);

        repo.Saved.Single().Reminders!.Overrides.Should().ContainSingle();
    }

    [Fact]
    public async Task ACalendarThatHasNeverSyncedReminders_IsForcedThroughAFullSync()
    {
        // The backfill. RemindersSyncedAt is null on every pre-existing row, and an incremental
        // sync would never re-send an unchanged event.
        var syncState = new SyncState { SyncToken = "still-valid", RemindersSyncedAt = null };

        var client = await RunSyncAsync(syncState);

        client.Verify(c => c.GetEventsAsync(
            It.IsAny<string>(),
            It.Is<DateTimeOffset?>(d => d.HasValue),   // a window is passed => full sync
            It.Is<DateTimeOffset?>(d => d.HasValue),
            null,                                       // ...and no sync token
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AfterTheBackfillRuns_RemindersSyncedAtIsStamped()
    {
        var syncState = new SyncState { SyncToken = "still-valid", RemindersSyncedAt = null };

        await RunSyncAsync(syncState);

        syncState.RemindersSyncedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ACalendarAlreadyStamped_SyncsIncrementally()
    {
        // The backfill must run exactly once per calendar, not on every sync.
        var syncState = new SyncState { SyncToken = "still-valid", RemindersSyncedAt = DateTimeOffset.UtcNow };

        var client = await RunSyncAsync(syncState);

        client.Verify(c => c.GetEventsAsync(
            It.IsAny<string>(), null, null, "still-valid", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnEchoedEventWhoseRemindersChanged_IsNotTreatedAsAnEcho()
    {
        // THE ONE THAT MATTERS. Someone adds a reminder on their phone within 60s of a kiosk write
        // to the same event. Google echoes the event back with the hash the kiosk stamped — which
        // covers Title/Start/End/IsAllDay/Description and NOT reminders — so the old guard drops
        // the change silently and permanently.
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.InheritsCalendarDefault };
        var echoed   = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           ContentHash = "hash-the-kiosk-just-wrote",
                                           Reminders = EventReminders.Explicit([new("popup", 15)]) };

        var repo = await SyncOneEventAsync(existing, echoed, recentlyWrittenHash: "hash-the-kiosk-just-wrote");

        repo.Saved.Single().Reminders!.Overrides.Should().ContainSingle()
            .Which.Minutes.Should().Be(15, "a reminder change made elsewhere must not be mistaken for our own echo");
    }

    [Fact]
    public async Task AGenuineEcho_IsStillSuppressed()
    {
        // The guard must keep doing its job: same hash AND same reminders => our own write.
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           Reminders = EventReminders.Explicit([new("popup", 15)]) };
        var echoed   = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           ContentHash = "hash-the-kiosk-just-wrote",
                                           Reminders = EventReminders.Explicit([new("popup", 15)]) };

        var repo = await SyncOneEventAsync(existing, echoed, recentlyWrittenHash: "hash-the-kiosk-just-wrote");

        repo.Saved.Should().BeEmpty("a genuine self-echo is still suppressed");
    }

    [Fact]
    public async Task AKioskCreatedEventsFirstEcho_PopulatesItsReminders()
    {
        // I2: CalendarEventService.CreateAsync discards Google's create response except the id, so
        // a kiosk-created row starts with Reminders = null. Google's echo of that very create is the
        // FIRST place its reminders (typically useDefault:true) ever arrive. The OLD guard read
        // "existing.Reminders is null" as "nothing to compare, assume echo" and discarded that echo
        // forever — incremental sync never re-sends an unchanged event, so the row kept Reminders =
        // null indefinitely and FHQ-191's timeline never saw it.
        var existing = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist", Reminders = null };
        var echoed   = new CalendarEvent { GoogleEventId = "g1", Title = "Dentist",
                                           ContentHash = "hash-the-kiosk-just-wrote",
                                           Reminders = EventReminders.InheritsCalendarDefault };

        var repo = await SyncOneEventAsync(existing, echoed, recentlyWrittenHash: "hash-the-kiosk-just-wrote");

        repo.Saved.Should().ContainSingle(
            "an event whose reminders were never learned locally must not be discarded as a self-echo");
        repo.Saved.Single().Reminders!.SameAs(EventReminders.InheritsCalendarDefault).Should().BeTrue();
    }

    // ── I1: an existing calendar's DefaultReminders (RefreshCalendarDefaultsAsync) ──────────
    // Mirrors CalendarSyncServiceTests.SyncAllAsync_ExistingCalendar_AdoptsGooglesDefaultZoneWithoutEverBlankingIt
    // (FHQ-164 rung 4) for the FHQ-189 field: AddCalendarAsync only ever runs for a BRAND NEW
    // calendar, so without this fix every calendar already in production keeps a null
    // DefaultReminders forever.

    [Fact]
    public async Task ExistingCalendar_AdoptsGooglesDefaultReminders_WhenNeverObservedBefore()
    {
        var (localCalendar, repo) = await RunCalendarDefaultsRefreshAsync(
            googleReminders: EventReminders.Explicit([new("popup", 30)]),
            storedReminders: null);

        localCalendar.DefaultReminders!.SameAs(EventReminders.Explicit([new("popup", 30)])).Should().BeTrue();
        repo.Verify(r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingCalendar_AdoptsGooglesDefaultReminders_GoogleIsSystemOfRecord()
    {
        var (localCalendar, repo) = await RunCalendarDefaultsRefreshAsync(
            googleReminders: EventReminders.Explicit([new("popup", 30)]),
            storedReminders: EventReminders.Explicit([new("email", 1440)]));

        localCalendar.DefaultReminders!.SameAs(EventReminders.Explicit([new("popup", 30)])).Should().BeTrue();
        repo.Verify(r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingCalendar_AbsentDefaultReminders_NeverBlanksAKnownValue()
    {
        var stored = EventReminders.Explicit([new("popup", 30)]);

        var (localCalendar, repo) = await RunCalendarDefaultsRefreshAsync(
            googleReminders: null,
            storedReminders: stored);

        localCalendar.DefaultReminders.Should().BeSameAs(stored);
        repo.Verify(r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingCalendar_SameDefaultReminders_IsIdempotent_NoWrite()
    {
        var (localCalendar, repo) = await RunCalendarDefaultsRefreshAsync(
            googleReminders: EventReminders.Explicit([new("popup", 30)]),
            storedReminders: EventReminders.Explicit([new("popup", 30)]));

        repo.Verify(r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static async Task<(CalendarInfo localCalendar, Mock<ICalendarRepository> repo)> RunCalendarDefaultsRefreshAsync(
        EventReminders? googleReminders, EventReminders? storedReminders)
    {
        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(30);
        var calendarId = Guid.NewGuid();
        const string googleCalendarId = "defaults@group.calendar.google.com";

        var googleCalendar = new CalendarInfo
        {
            Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Defaults",
            DefaultReminders = googleReminders
        };
        var localCalendar = new CalendarInfo
        {
            Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Defaults",
            DefaultReminders = storedReminders
        };

        var (client, repo, sut) = CreateSut(new Mock<IOutboundWriteHashCache>());

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { googleCalendar });
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { localCalendar });
        repo.Setup(r => r.GetCalendarByIdAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localCalendar);
        repo.Setup(r => r.GetSyncStateAsync(calendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(googleCalendarId, startDate, endDate, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(calendarId, startDate, endDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        await sut.SyncAllAsync(startDate, endDate);

        return (localCalendar, repo);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    // Mirrors CalendarSyncServiceTests.CreateSutWithAllDeps: same mocked dependency set, wired
    // fresh per call so tests don't share mutable mock state.

    private sealed class RepoCapture
    {
        public List<CalendarEvent> Saved { get; } = [];
    }

    /// <summary>
    /// Wires a single-event incremental sync: <paramref name="existing"/> is what
    /// <c>GetEventByGoogleEventIdAsync</c> returns for the fetched event's Google id, and
    /// <paramref name="fetched"/> is the sole event Google returns this sync. When
    /// <paramref name="recentlyWrittenHash"/> is supplied, the outbound-write cache reports that
    /// hash as recently written (modelling a kiosk write within the FHQ-30 60s TTL window).
    /// Returns everything the repository was asked to persist (Add or Update) this sync.
    /// </summary>
    private static async Task<RepoCapture> SyncOneEventAsync(
        CalendarEvent existing, CalendarEvent fetched, string? recentlyWrittenHash = null)
    {
        var calendar = new CalendarInfo { Id = CalendarId, GoogleCalendarId = GoogleCalendarId, DisplayName = "Reminders" };
        // Already backfilled, so this exercises plain incremental sync rather than the FHQ-189 backfill path.
        var syncState = new SyncState { CalendarInfoId = CalendarId, SyncToken = "tok", RemindersSyncedAt = DateTimeOffset.UtcNow };

        var outboundCacheMock = new Mock<IOutboundWriteHashCache>();
        outboundCacheMock
            .Setup(c => c.WasRecentlyWritten(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string _, string hash) => recentlyWrittenHash is not null && hash == recentlyWrittenHash);

        var (client, repo, sut) = CreateSut(outboundCacheMock);

        repo.Setup(r => r.GetCalendarByIdAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        repo.Setup(r => r.GetSyncStateAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(syncState);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarInfo> { calendar });
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(fetched.GoogleEventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        client.Setup(c => c.GetEventsAsync(GoogleCalendarId, null, null, "tok", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { fetched }, "tok2"));

        var capture = new RepoCapture();
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<CalendarEvent, CancellationToken>((e, _) => capture.Saved.Add(e));
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<CalendarEvent, CancellationToken>((e, _) => capture.Saved.Add(e));

        var start = DateTimeOffset.UtcNow.AddDays(-30);
        var end   = DateTimeOffset.UtcNow.AddDays(30);
        await sut.SyncAsync(CalendarId, start, end);

        return capture;
    }

    /// <summary>
    /// Runs one calendar's sync with no events returned by Google, using the supplied
    /// <paramref name="syncState"/> as-is (so the caller controls <see cref="SyncState.SyncToken"/>
    /// and <see cref="SyncState.RemindersSyncedAt"/> to drive the full-sync/backfill decision).
    /// Returns the Google client mock so the caller can verify how <c>GetEventsAsync</c> was called.
    /// </summary>
    private static async Task<Mock<IGoogleCalendarClient>> RunSyncAsync(SyncState syncState)
    {
        var calendar = new CalendarInfo { Id = CalendarId, GoogleCalendarId = GoogleCalendarId, DisplayName = "Reminders" };

        var (client, repo, sut) = CreateSut(new Mock<IOutboundWriteHashCache>());

        repo.Setup(r => r.GetCalendarByIdAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(calendar);
        repo.Setup(r => r.GetSyncStateAsync(CalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(syncState);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CalendarInfo> { calendar });
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(
                CalendarId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        client.Setup(c => c.GetEventsAsync(
                GoogleCalendarId,
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "next-token"));

        var start = DateTimeOffset.UtcNow.AddDays(-30);
        var end   = DateTimeOffset.UtcNow.AddDays(30);
        await sut.SyncAsync(CalendarId, start, end);

        return client;
    }

    private static (Mock<IGoogleCalendarClient> client, Mock<ICalendarRepository> repo, CalendarSyncService sut) CreateSut(
        Mock<IOutboundWriteHashCache> outboundCacheMock)
    {
        var clientMock          = new Mock<IGoogleCalendarClient>();
        var repoMock            = new Mock<ICalendarRepository>();
        var tagParserMock       = new Mock<IMemberTagParser>();
        var loggerMock          = new Mock<ILogger<CalendarSyncService>>();
        var tokenStoreMock      = new Mock<ITokenStore>();
        var currentUserMock     = new Mock<ICurrentUserService>();
        var syncFailureRepoMock = new Mock<ISyncFailureRepository>();

        currentUserMock.SetupGet(c => c.UserId).Returns("test-user");
        tagParserMock
            .Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new List<string>());

        var sut = new CalendarSyncService(
            clientMock.Object,
            repoMock.Object,
            tagParserMock.Object,
            loggerMock.Object,
            tokenStoreMock.Object,
            currentUserMock.Object,
            syncFailureRepoMock.Object,
            outboundCacheMock.Object);

        return (clientMock, repoMock, sut);
    }
}
