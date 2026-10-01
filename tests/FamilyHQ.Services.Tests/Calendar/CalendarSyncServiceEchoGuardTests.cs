using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// Tests for the webhook self-echo guard wired into CalendarSyncService.SyncCoreAsync (FHQ-30).
/// </summary>
public class CalendarSyncServiceEchoGuardTests
{
    private static readonly Guid _calendarInfoId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DateTimeOffset _start = new(2026, 5, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _end   = _start.AddDays(30);

    private (Mock<IGoogleCalendarClient> google,
             Mock<ICalendarRepository> calendarRepo,
             Mock<IOutboundWriteHashCache> outboundCache,
             RecordingLogger<CalendarSyncService> logger,
             CalendarSyncService sut) CreateSut()
    {
        var google          = new Mock<IGoogleCalendarClient>();
        var calendarRepo    = new Mock<ICalendarRepository>();
        var tagParser       = new Mock<IMemberTagParser>();
        var tokenStore      = new Mock<ITokenStore>();
        var currentUser     = new Mock<ICurrentUserService>();
        var syncFailureRepo = new Mock<ISyncFailureRepository>();
        var outboundCache   = new Mock<IOutboundWriteHashCache>();
        var logger          = new RecordingLogger<CalendarSyncService>();

        currentUser.SetupGet(c => c.UserId).Returns("test-user");
        tagParser.Setup(p => p.ParseMembers(It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                 .Returns(new List<string>());

        // Wire up a calendar for every test
        var calendarInfo = new CalendarInfo
        {
            Id = _calendarInfoId,
            GoogleCalendarId = "cal@group.calendar.google.com",
            DisplayName = "Test Cal",
            IsShared = false
        };
        calendarRepo.Setup(r => r.GetCalendarByIdAsync(_calendarInfoId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(calendarInfo);
        // FHQ-189: RemindersSyncedAt must be stamped, or every test here gets silently promoted to
        // a forced backfill full sync instead of the incremental sync these echo-guard tests exist
        // to exercise. This helper models an already-backfilled calendar.
        calendarRepo.Setup(r => r.GetSyncStateAsync(_calendarInfoId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SyncState { CalendarInfoId = _calendarInfoId, SyncToken = "tok", RemindersSyncedAt = DateTimeOffset.UtcNow });
        calendarRepo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<CalendarInfo> { calendarInfo });

        var sut = new CalendarSyncService(
            google.Object,
            calendarRepo.Object,
            tagParser.Object,
            logger,
            tokenStore.Object,
            currentUser.Object,
            syncFailureRepo.Object,
            outboundCache.Object);

        return (google, calendarRepo, outboundCache, logger, sut);
    }

    // ── Test 1: matching hash → event is skipped ─────────────────────────────

    [Fact]
    public async Task SyncAsync_EventHashMatchesCache_SkipsEvent()
    {
        // Arrange
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-1",
            Title         = "Lunch",
            Start         = _start.AddHours(1),
            End           = _start.AddHours(2),
            ContentHash   = "matching-hash"
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-1", "matching-hash"))
                     .Returns(true);

        // The row we stored when we made that write. A matching stamp only nominates a candidate;
        // the guard confirms it against what we hold, so the echo has to carry the stored content.
        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-1", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CalendarEvent
                    {
                        GoogleEventId = "google-evt-1",
                        Title         = "Lunch",
                        Start         = _start.AddHours(1),
                        End           = _start.AddHours(2)
                    });

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — the candidate costs exactly one indexed lookup and nothing else
        calendarRepo.Verify(
            r => r.GetEventByGoogleEventIdAsync("google-evt-1", It.IsAny<CancellationToken>()),
            Times.Once,
            "a hash candidate is confirmed against the stored row, which is read once");
        calendarRepo.Verify(
            r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not be inserted");
        calendarRepo.Verify(
            r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not be updated");

        // Assert — Information-level "Self-echo skipped" log entry is produced
        var skipLog = logger.Records.FirstOrDefault(r =>
            r.Level == LogLevel.Information &&
            r.Message.Contains("Self-echo skipped"));

        skipLog.Should().NotBeNull("the guard must emit a Self-echo skipped log entry");
        skipLog!.Message.Should().Contain("google-evt-1", "log must include the EventId");
    }

    // ── Test 2: hash present but not in cache → event flows through normally ─

    [Fact]
    public async Task SyncAsync_EventHashDoesNotMatchCache_ProcessesEvent()
    {
        // Arrange
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-2",
            Title         = "Stand-up",
            Start         = _start.AddHours(3),
            End           = _start.AddHours(4),
            ContentHash   = "unknown-hash"
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-2", "unknown-hash"))
                     .Returns(false);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-2", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((CalendarEvent?)null);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — event was not skipped; DB add was called
        calendarRepo.Verify(
            r => r.AddEventAsync(
                It.Is<CalendarEvent>(e => e.GoogleEventId == "google-evt-2"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a cache-miss event must flow through to AddEventAsync");

        // No skip log
        logger.Records.Should().NotContain(
            r => r.Message.Contains("Self-echo skipped"),
            "a cache-miss event must not produce a skip log");
    }

    // ── Test 3: null hash → cache NOT consulted, event flows through ─────────

    [Fact]
    public async Task SyncAsync_EventHashIsNull_SkipsCacheLookupAndProcessesEvent()
    {
        // Arrange — manually-edited Google event, legacy event, or delete tombstone
        // that carries no ContentHash extended property
        var (google, calendarRepo, outboundCache, _, sut) = CreateSut();

        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-3",
            Title         = "Manual edit",
            Start         = _start.AddHours(5),
            End           = _start.AddHours(6),
            ContentHash   = null   // no extended property on inbound event
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-3", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((CalendarEvent?)null);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — cache was never consulted (null hash short-circuits the guard)
        outboundCache.Verify(
            c => c.WasRecentlyWritten(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "null ContentHash must bypass the cache entirely");

        // Assert — event flowed through to the DB
        calendarRepo.Verify(
            r => r.AddEventAsync(
                It.Is<CalendarEvent>(e => e.GoogleEventId == "google-evt-3"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "an event with null ContentHash must be processed normally");
    }

    // ── The stamp proves authorship, not freshness ───────────────────────────
    //
    // Google can change an event's content without touching the content-hash it holds: patching a
    // series master rewrites its exceptions' summaries, and an edit from the Google Calendar app on
    // a phone touches no extended property at all. Both arrive inside the cache's 60-second window
    // carrying the stamp our own earlier write left there, so the hash test alone would discard a
    // real change permanently — incremental sync never re-sends an unchanged event.

    [Fact]
    public async Task SyncAsync_HashMatchesButGoogleRewroteTheTitle_ProcessesEvent()
    {
        // Arrange — the occurrence the family singled out ("Dentist") after the series was renamed
        // to "Training camp" at all-events scope. Google propagated the master's summary onto the
        // exception and left its extendedProperties, and therefore its stamp, alone.
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundException = new CalendarEvent
        {
            GoogleEventId          = "google-evt-4_20261007T180000Z",
            GoogleRecurringEventId = "google-evt-4",
            Title                  = "Training camp",
            Start                  = _start.AddHours(7),
            End                    = _start.AddHours(8),
            OriginalStartTime      = _start.AddHours(7),
            ContentHash            = "hash-of-the-single-occurrence-write"
        };

        var storedOverride = new CalendarEvent
        {
            GoogleEventId          = "google-evt-4_20261007T180000Z",
            GoogleRecurringEventId = "google-evt-4",
            Title                  = "Dentist",
            Start                  = _start.AddHours(7),
            End                    = _start.AddHours(8),
            OriginalStartTime      = _start.AddHours(7),
            RecurrenceRule         = "FREQ=WEEKLY;COUNT=3"
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundException }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten(
                         "google-evt-4_20261007T180000Z", "hash-of-the-single-occurrence-write"))
                     .Returns(true);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync(
                         "google-evt-4_20261007T180000Z", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(storedOverride);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — the rename reached the row that the kiosk renders
        storedOverride.Title.Should().Be(
            "Training camp",
            "Google's propagated rename is a real inbound change, not an echo of our own write");

        calendarRepo.Verify(
            r => r.UpdateEventAsync(storedOverride, It.IsAny<CancellationToken>()),
            Times.Once,
            "the changed event must be persisted");

        logger.Records.Should().NotContain(
            r => r.Message.Contains("Self-echo skipped"),
            "an event whose content no longer matches the stored row is not an echo");
    }

    [Fact]
    public async Task SyncAsync_HashMatchesButGoogleMovedTheStart_ProcessesEvent()
    {
        // Arrange — the same defect without recurrence in it: someone moved the event an hour later
        // in the Google Calendar app within a minute of the kiosk saving it.
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-5",
            Title         = "Lunch",
            Start         = _start.AddHours(13),
            End           = _start.AddHours(14),
            ContentHash   = "kiosk-hash"
        };

        var storedEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-5",
            Title         = "Lunch",
            Start         = _start.AddHours(12),
            End           = _start.AddHours(13)
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-5", "kiosk-hash")).Returns(true);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-5", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(storedEvent);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert
        storedEvent.Start.Should().Be(_start.AddHours(13), "the move must reach the stored row");
        storedEvent.End.Should().Be(_start.AddHours(14));

        logger.Records.Should().NotContain(r => r.Message.Contains("Self-echo skipped"));
    }

    [Fact]
    public async Task SyncAsync_HashMatchesButGoogleChangedTheLocation_ProcessesEvent()
    {
        // Arrange — Location is not in the hash, so a location changed elsewhere inside the window
        // leaves the stamp exactly where our own write left it. The stored row still knows.
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-8",
            Title         = "Swimming",
            Start         = _start.AddHours(15),
            End           = _start.AddHours(16),
            Location      = "The other pool",
            ContentHash   = "kiosk-hash"
        };

        var storedEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-8",
            Title         = "Swimming",
            Start         = _start.AddHours(15),
            End           = _start.AddHours(16),
            Location      = "The pool"
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-8", "kiosk-hash")).Returns(true);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-8", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(storedEvent);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert
        storedEvent.Location.Should().Be("The other pool");
        logger.Records.Should().NotContain(r => r.Message.Contains("Self-echo skipped"));
    }

    // ── Loop protection: a genuine echo is still skipped ─────────────────────

    [Fact]
    public async Task SyncAsync_HashMatchesAndContentIsUnchanged_SkipsEvent()
    {
        // Arrange — Google's echo of our own write, in the shape it actually comes back in: the same
        // instant expressed at a different UTC offset, an absent description reported as null where
        // the row stores an empty string, and a missing location echoed as "" where the row keeps
        // null (the outbound mapping sends `location ?? ""`). None of these is a change, and
        // treating any of them as one would send FamilyHQ's own writes back through the sync as
        // inbound work on every save.
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEcho = new CalendarEvent
        {
            GoogleEventId = "google-evt-6",
            Title         = "Swimming",
            Start         = new DateTimeOffset(2026, 5, 20, 19, 0, 0, TimeSpan.FromHours(1)),
            End           = new DateTimeOffset(2026, 5, 20, 20, 0, 0, TimeSpan.FromHours(1)),
            Description   = null,
            Location      = string.Empty,
            ContentHash   = "kiosk-hash"
        };

        var storedEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-6",
            Title         = "Swimming",
            Start         = new DateTimeOffset(2026, 5, 20, 18, 0, 0, TimeSpan.Zero),
            End           = new DateTimeOffset(2026, 5, 20, 19, 0, 0, TimeSpan.Zero),
            Description   = string.Empty,
            Location      = null
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEcho }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-6", "kiosk-hash")).Returns(true);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-6", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(storedEvent);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert
        calendarRepo.Verify(
            r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a genuine echo must still be skipped");
        calendarRepo.Verify(
            r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Information && r.Message.Contains("Self-echo skipped"),
            "the guard must still recognise FamilyHQ's own write coming back");
    }

    [Fact]
    public async Task SyncAsync_HashMatchesAndNothingIsStoredYet_SkipsEvent()
    {
        // Arrange — the create race: Google's echo of a create can arrive before our own insert has
        // committed. There is no row to confirm against and nothing to lose by trusting the stamp,
        // because the create path holds the content it just wrote.
        var (google, calendarRepo, outboundCache, logger, sut) = CreateSut();

        var inboundEcho = new CalendarEvent
        {
            GoogleEventId = "google-evt-7",
            Title         = "Brand new",
            Start         = _start.AddHours(9),
            End           = _start.AddHours(10),
            ContentHash   = "kiosk-hash"
        };

        google.Setup(g => g.GetEventsAsync(
                   It.IsAny<string>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<DateTimeOffset?>(),
                   It.IsAny<string?>(),
                   It.IsAny<CancellationToken>()))
              .ReturnsAsync((new List<CalendarEvent> { inboundEcho }, "next-tok"));

        outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-7", "kiosk-hash")).Returns(true);

        calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-7", It.IsAny<CancellationToken>()))
                    .ReturnsAsync((CalendarEvent?)null);

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert
        calendarRepo.Verify(
            r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the echo of a create must not be inserted a second time");

        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Information && r.Message.Contains("Self-echo skipped"));
    }
}
