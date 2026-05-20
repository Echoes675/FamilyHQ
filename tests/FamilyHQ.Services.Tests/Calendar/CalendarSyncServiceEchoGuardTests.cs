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
        tagParser.Setup(p => p.ParseMembers(It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>()))
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
        calendarRepo.Setup(r => r.GetSyncStateAsync(_calendarInfoId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SyncState { CalendarInfoId = _calendarInfoId, SyncToken = "tok" });
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

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — no DB interaction at all for the skipped event
        calendarRepo.Verify(
            r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not trigger any DB lookup");
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
}
