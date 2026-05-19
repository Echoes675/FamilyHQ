using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// Tests for the webhook self-echo guard wired into CalendarSyncService.SyncCoreAsync (FHQ-30).
/// </summary>
public class CalendarSyncServiceEchoGuardTests
{
    private readonly Mock<IGoogleCalendarClient> _google         = new();
    private readonly Mock<ICalendarRepository>   _calendarRepo   = new();
    private readonly Mock<IMemberTagParser>       _tagParser      = new();
    private readonly Mock<ITokenStore>            _tokenStore     = new();
    private readonly Mock<ICurrentUserService>    _currentUser    = new();
    private readonly Mock<ISyncFailureRepository> _syncFailureRepo = new();
    private readonly Mock<IOutboundWriteHashCache> _outboundCache  = new();
    private readonly RecordingLogger<CalendarSyncService> _logger = new();

    private static readonly Guid _calendarInfoId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DateTimeOffset _start = new(2026, 5, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _end   = _start.AddDays(30);

    public CalendarSyncServiceEchoGuardTests()
    {
        _currentUser.SetupGet(c => c.UserId).Returns("test-user");
        _tagParser.Setup(p => p.ParseMembers(It.IsAny<string?>(), It.IsAny<IReadOnlyList<string>>()))
                  .Returns(new List<string>());

        // Wire up a calendar for every test
        var calendarInfo = new CalendarInfo
        {
            Id = _calendarInfoId,
            GoogleCalendarId = "cal@group.calendar.google.com",
            DisplayName = "Test Cal",
            IsShared = false
        };
        _calendarRepo.Setup(r => r.GetCalendarByIdAsync(_calendarInfoId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(calendarInfo);
        _calendarRepo.Setup(r => r.GetSyncStateAsync(_calendarInfoId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new SyncState { CalendarInfoId = _calendarInfoId, SyncToken = "tok" });
        _calendarRepo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<CalendarInfo> { calendarInfo });
    }

    private CalendarSyncService CreateSut() => new(
        _google.Object,
        _calendarRepo.Object,
        _tagParser.Object,
        _logger,
        _tokenStore.Object,
        _currentUser.Object,
        _syncFailureRepo.Object,
        _outboundCache.Object);

    // ── Test 1: matching hash → event is skipped ─────────────────────────────

    [Fact]
    public async Task SyncCoreAsync_skips_event_whose_id_and_hash_match_outbound_cache()
    {
        // Arrange
        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-1",
            Title         = "Lunch",
            Start         = _start.AddHours(1),
            End           = _start.AddHours(2),
            ContentHash   = "matching-hash"
        };

        _google.Setup(g => g.GetEventsAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
               .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        _outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-1", "matching-hash"))
                      .Returns(true);

        var sut = CreateSut();

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — no DB interaction at all for the skipped event
        _calendarRepo.Verify(
            r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not trigger any DB lookup");
        _calendarRepo.Verify(
            r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not be inserted");
        _calendarRepo.Verify(
            r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped event must not be updated");

        // Assert — Information-level "Self-echo skipped" log entry is produced
        var skipLog = _logger.Records.FirstOrDefault(r =>
            r.Level == LogLevel.Information &&
            r.Message.Contains("Self-echo skipped"));

        skipLog.Should().NotBeNull("the guard must emit a Self-echo skipped log entry");
        skipLog!.Message.Should().Contain("google-evt-1", "log must include the EventId");
    }

    // ── Test 2: hash present but not in cache → event flows through normally ─

    [Fact]
    public async Task SyncCoreAsync_processes_event_normally_when_hash_does_not_match_cache()
    {
        // Arrange
        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-2",
            Title         = "Stand-up",
            Start         = _start.AddHours(3),
            End           = _start.AddHours(4),
            ContentHash   = "unknown-hash"
        };

        _google.Setup(g => g.GetEventsAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
               .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        _outboundCache.Setup(c => c.WasRecentlyWritten("google-evt-2", "unknown-hash"))
                      .Returns(false);

        _calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-2", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((CalendarEvent?)null);

        var sut = CreateSut();

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — event was not skipped; DB add was called
        _calendarRepo.Verify(
            r => r.AddEventAsync(
                It.Is<CalendarEvent>(e => e.GoogleEventId == "google-evt-2"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a cache-miss event must flow through to AddEventAsync");

        // No skip log
        _logger.Records.Should().NotContain(
            r => r.Message.Contains("Self-echo skipped"),
            "a cache-miss event must not produce a skip log");
    }

    // ── Test 3: null hash → cache NOT consulted, event flows through ─────────

    [Fact]
    public async Task SyncCoreAsync_processes_event_normally_when_inbound_hash_is_null()
    {
        // Arrange — manually-edited Google event, legacy event, or delete tombstone
        // that carries no ContentHash extended property
        var inboundEvent = new CalendarEvent
        {
            GoogleEventId = "google-evt-3",
            Title         = "Manual edit",
            Start         = _start.AddHours(5),
            End           = _start.AddHours(6),
            ContentHash   = null   // no extended property on inbound event
        };

        _google.Setup(g => g.GetEventsAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<DateTimeOffset?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
               .ReturnsAsync((new List<CalendarEvent> { inboundEvent }, "next-tok"));

        _calendarRepo.Setup(r => r.GetEventByGoogleEventIdAsync("google-evt-3", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((CalendarEvent?)null);

        var sut = CreateSut();

        // Act
        await sut.SyncAsync(_calendarInfoId, _start, _end, CancellationToken.None);

        // Assert — cache was never consulted (null hash short-circuits the guard)
        _outboundCache.Verify(
            c => c.WasRecentlyWritten(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "null ContentHash must bypass the cache entirely");

        // Assert — event flowed through to the DB
        _calendarRepo.Verify(
            r => r.AddEventAsync(
                It.Is<CalendarEvent>(e => e.GoogleEventId == "google-evt-3"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "an event with null ContentHash must be processed normally");
    }
}

/// <summary>
/// Simple test logger that captures log records so assertions can inspect them.
/// Avoids the need for a third-party FakeLogger package while providing the
/// same verification surface.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _records = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Records => _records;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _records.Add((logLevel, formatter(state, exception)));
    }
}
