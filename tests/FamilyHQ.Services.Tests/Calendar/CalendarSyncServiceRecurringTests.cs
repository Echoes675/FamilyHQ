using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-18.2 — two-pass RRULE ingestion: a per-run cache fetches each unknown series
/// master exactly once, series with a stored RRULE skip pass 2 entirely, pass-2
/// failures degrade gracefully (RecurrenceRule = null, retry next sync), cancelled
/// instances are deleted, and the FHQ-30 echo guard still short-circuits echoed
/// recurring instances.
/// </summary>
public class CalendarSyncServiceRecurringTests
{
    private const string SeriesId = "series-master-id";

    [Fact]
    public async Task SyncAsync_FiveInstanceSeries_FetchesSeriesMasterOnce()
    {
        // Arrange — five instances of one series, none with a stored RRULE. Pass 2 must call
        // GetSeriesMasterAsync exactly once and stamp the resolved RRULE on every instance.
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-series");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var googleCalendarId = "series@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Series Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: true);

        var instances = Enumerable.Range(0, 5)
            .Select(i => new CalendarEvent
            {
                GoogleEventId          = $"inst-{i}",
                Title                  = "Weekly",
                GoogleRecurringEventId = SeriesId
            })
            .ToList();
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((instances.Cast<CalendarEvent>().ToList(), "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=MO", new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero)));

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — master fetched once, every instance stamped with the RRULE
        client.Verify(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleRecurringEventId == SeriesId && e.RecurrenceRule == "RRULE:FREQ=WEEKLY;BYDAY=MO"),
            It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task SyncAsync_WhenSeriesRRuleAlreadyStored_SkipsPassTwo()
    {
        // Arrange — a later sync where the RRULE is already in the DB for this series.
        // Pass 2 must be skipped entirely (no GetSeriesMasterAsync call) and the stored
        // RRULE re-applied to the instances.
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-stored");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var googleCalendarId = "stored@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Stored Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: false, syncToken: "incremental");

        var instance = new CalendarEvent { GoogleEventId = "inst-0", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "incremental", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { instance }, "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { [SeriesId] = "RRULE:FREQ=WEEKLY;BYDAY=MO" });

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — pass 2 entirely skipped; stored RRULE re-applied
        client.Verify(c => c.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleRecurringEventId == SeriesId && e.RecurrenceRule == "RRULE:FREQ=WEEKLY;BYDAY=MO"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenSeriesMasterFetchReturnsNull_PersistsWithNullRRuleAndDoesNotFail()
    {
        // Arrange — transient pass-2 failure (null master). The instance must still persist with
        // RecurrenceRule = null so the next sync retries, and the sync as a whole must complete.
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-null-master");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var googleCalendarId = "null-master@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Null Master Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: true);

        var instance = new CalendarEvent { GoogleEventId = "inst-0", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { instance }, "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeriesMaster?)null);

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — instance persisted with null RRULE, sync token still advanced
        repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleRecurringEventId == SeriesId && e.RecurrenceRule == null),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddSyncStateAsync(It.Is<SyncState>(s => s.SyncToken == "tok"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenSeriesMasterFetchThrows_PersistsWithNullRRuleAndDoesNotFail()
    {
        // Arrange — transient API error on the master fetch must not abort the whole sync.
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-throw-master");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var googleCalendarId = "throw-master@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Throw Master Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: true);

        var instance = new CalendarEvent { GoogleEventId = "inst-0", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { instance }, "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleApiException(System.Net.HttpStatusCode.ServiceUnavailable, "GetSeriesMaster", "boom"));

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert
        repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleRecurringEventId == SeriesId && e.RecurrenceRule == null),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddSyncStateAsync(It.Is<SyncState>(s => s.SyncToken == "tok"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenSeriesMasterFetchThrowsReauth_PropagatesNotSwallowed()
    {
        // Arrange — a reauth failure on the master fetch is NOT a transient pass-2 failure;
        // it must propagate so the user is prompted to reconnect (consistent with pass 1).
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-reauth-master");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000005");
        var googleCalendarId = "reauth-master@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Reauth Master Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: true);

        var instance = new CalendarEvent { GoogleEventId = "inst-0", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { instance }, "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "revoked"));

        // Act
        var act = () => sut.SyncAsync(calendarId, start, end);

        // Assert
        await act.Should().ThrowAsync<GoogleReauthRequiredException>();
    }

    [Fact]
    public async Task SyncAsync_WhenRecurringInstanceCancelled_DeletesLocalRow()
    {
        // Arrange — an inbound cancelled recurring instance (single-instance cancellation) must
        // delete the corresponding local row via the existing tombstone reconciliation path.
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-cancel-inst");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000006");
        var googleCalendarId = "cancel-inst@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);
        var localRowId       = Guid.Parse("20000000-0000-0000-0000-000000000006");

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Cancel Inst Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: false, syncToken: "incremental");

        // Google sends cancelled instances as a tombstone (Title == CANCELLED_TOMBSTONE),
        // exactly as GoogleCalendarClient maps a status=="cancelled" item.
        var cancelled = new CalendarEvent { GoogleEventId = "inst-gone", Title = "CANCELLED_TOMBSTONE", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "incremental", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { cancelled }, "tok"));

        var trackedRow = new CalendarEvent { Id = localRowId, GoogleEventId = "inst-gone", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        repo.Setup(r => r.GetEventByGoogleEventIdAsync("inst-gone", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedRow);
        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — local row deleted, no master fetch needed for a cancellation-only batch
        repo.Verify(r => r.DeleteEventAsync(localRowId, It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenRecurringInstanceIsSelfEcho_SkippedByGuard()
    {
        // Arrange — FHQ-30 echo guard preservation: an echoed recurring instance (matching a
        // recently-written outbound hash) must be skipped, NOT persisted, and must not trigger
        // a pass-2 master fetch.
        var (client, repo, _, _, _, _, _, sut, outboundCache) = CreateSutWithEchoDeps("u-echo");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000007");
        var googleCalendarId = "echo@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Echo Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: false, syncToken: "incremental");

        var echoed = new CalendarEvent
        {
            GoogleEventId          = "inst-echo",
            Title                  = "Weekly",
            GoogleRecurringEventId = SeriesId,
            ContentHash            = "hash-abc"
        };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "incremental", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { echoed }, "tok"));

        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        outboundCache.Setup(o => o.WasRecentlyWritten("inst-echo", "hash-abc")).Returns(true);

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — echoed instance neither persisted nor master-fetched
        repo.Verify(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenMasterFetchFailsButRRuleAlreadyStoredOnRow_PreservesStoredRRule()
    {
        // Arrange — an existing local row already carries an RRULE, but this run's pass-2
        // resolution comes up empty (cache miss + null master). The transient failure must NOT
        // blank the row's known rule: the update path keeps existing.RecurrenceRule (the `??`).
        var (client, repo, _, _, _, _, _, sut) = CreateSutWithAllDeps("u-preserve-rrule");
        var calendarId       = Guid.Parse("10000000-0000-0000-0000-000000000008");
        var googleCalendarId = "preserve@google.com";
        var start            = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var end              = start.AddDays(60);

        var calendar = new CalendarInfo { Id = calendarId, GoogleCalendarId = googleCalendarId, DisplayName = "Preserve Cal" };
        ArrangeCalendar(repo, calendar, isFullSync: false, syncToken: "incremental");

        var incoming = new CalendarEvent { GoogleEventId = "inst-0", Title = "Weekly", GoogleRecurringEventId = SeriesId };
        client.Setup(c => c.GetEventsAsync(googleCalendarId, null, null, "incremental", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { incoming }, "tok"));

        // Existing tracked row with a known RRULE from an earlier sync.
        var existingRow = new CalendarEvent
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000008"),
            GoogleEventId = "inst-0", Title = "Weekly",
            GoogleRecurringEventId = SeriesId, RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=MO"
        };
        repo.Setup(r => r.GetEventByGoogleEventIdAsync("inst-0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRow);
        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetSeriesMasterAsync(googleCalendarId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeriesMaster?)null);

        // Act
        await sut.SyncAsync(calendarId, start, end);

        // Assert — the stored rule survives the failed re-fetch
        repo.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-0" && e.RecurrenceRule == "RRULE:FREQ=WEEKLY;BYDAY=MO"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void ArrangeCalendar(
        Mock<ICalendarRepository> repo, CalendarInfo calendar, bool isFullSync, string? syncToken = null)
    {
        repo.Setup(r => r.GetCalendarByIdAsync(calendar.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        repo.Setup(r => r.GetSyncStateAsync(calendar.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isFullSync ? (SyncState?)null : new SyncState { CalendarInfoId = calendar.Id, SyncToken = syncToken });
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { calendar });
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(calendar.Id, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, Mock<ILogger<CalendarSyncService>> logger,
        Mock<ITokenStore> tokenStore, Mock<ICurrentUserService> currentUser,
        Mock<ISyncFailureRepository> syncFailureRepo,
        CalendarSyncService sut) CreateSutWithAllDeps(string userId)
    {
        var (g, r, tp, l, ts, cu, sf, sut, _) = CreateSutWithEchoDeps(userId);
        return (g, r, tp, l, ts, cu, sf, sut);
    }

    private (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo,
        Mock<IMemberTagParser> tagParser, Mock<ILogger<CalendarSyncService>> logger,
        Mock<ITokenStore> tokenStore, Mock<ICurrentUserService> currentUser,
        Mock<ISyncFailureRepository> syncFailureRepo,
        CalendarSyncService sut, Mock<IOutboundWriteHashCache> outboundCache) CreateSutWithEchoDeps(string userId)
    {
        var clientMock          = new Mock<IGoogleCalendarClient>();
        var repoMock            = new Mock<ICalendarRepository>();
        var tagParserMock       = new Mock<IMemberTagParser>();
        var loggerMock          = new Mock<ILogger<CalendarSyncService>>();
        var tokenStoreMock      = new Mock<ITokenStore>();
        var currentUserMock     = new Mock<ICurrentUserService>();
        var syncFailureRepoMock = new Mock<ISyncFailureRepository>();
        var outboundCacheMock   = new Mock<IOutboundWriteHashCache>();
        currentUserMock.SetupGet(c => c.UserId).Returns(userId);

        tagParserMock.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new List<string>());

        var sut = new CalendarSyncService(
            clientMock.Object, repoMock.Object, tagParserMock.Object,
            loggerMock.Object, tokenStoreMock.Object, currentUserMock.Object,
            syncFailureRepoMock.Object, outboundCacheMock.Object);

        return (clientMock, repoMock, tagParserMock, loggerMock, tokenStoreMock, currentUserMock, syncFailureRepoMock, sut, outboundCacheMock);
    }
}
