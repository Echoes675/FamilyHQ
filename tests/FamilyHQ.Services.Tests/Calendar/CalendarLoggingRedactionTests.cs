using FamilyHQ.Core.DTOs;
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
/// FHQ-166. The calendar sync/write path used to put two PII values into Seq on ordinary,
/// non-failure operations:
/// <list type="bullet">
///   <item><description>
///     <c>CalendarInfo.GoogleCalendarId</c> — a Google PRIMARY calendar's id <b>is</b> the account's
///     email address. Nothing about the type <c>string GoogleCalendarId</c> says so, which is
///     exactly why it went unnoticed.
///   </description></item>
///   <item><description>
///     <c>CalendarInfo.DisplayName</c> — the calendar's Google <c>summary</c>, which is the email
///     address again for a primary calendar and a family member's name for a member calendar.
///   </description></item>
/// </list>
/// Each test drives a real operation and asserts on everything the service logged: the sensitive
/// value is absent, and FamilyHQ's own <c>CalendarInfo.Id</c> is present so the line still
/// correlates. Deleting the value without leaving a correlation key would have traded a disclosure
/// problem for a diagnosis problem.
/// </summary>
public class CalendarLoggingRedactionTests
{
    private static readonly Guid OwnerCalendarId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SharedCalendarId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberCalendarId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // Google returns the account's own address as the primary calendar's id AND as its summary.
    private const string PrimaryCalendarId = "a.family.member@example.com";
    private const string PrimaryDisplayName = "a.family.member@example.com";
    private const string SharedCalendarGoogleId = "sentinelshared@group.calendar.google.com";
    private const string MemberDisplayName = "Sentinelchild";

    private static CalendarInfo OwnerCalendar() => new()
    {
        Id = OwnerCalendarId,
        GoogleCalendarId = PrimaryCalendarId,
        DisplayName = PrimaryDisplayName,
        IsShared = false
    };

    private static CalendarInfo SharedCalendar() => new()
    {
        Id = SharedCalendarId,
        GoogleCalendarId = SharedCalendarGoogleId,
        DisplayName = "Sentinelfamily",
        IsShared = true
    };

    private static CalendarInfo MemberCalendar() => new()
    {
        Id = MemberCalendarId,
        GoogleCalendarId = "sentinelchild@example.com",
        DisplayName = MemberDisplayName,
        IsShared = false
    };

    // ── CalendarSyncService ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_LogsNeitherTheGoogleCalendarIdNorTheDisplayName()
    {
        var (client, repo, logger, sut) = CreateSyncSut();
        var calendar = OwnerCalendar();
        var (start, end) = Window();

        repo.Setup(r => r.GetCalendarByIdAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendar]);
        repo.Setup(r => r.GetSyncStateAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(OwnerCalendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        client.Setup(c => c.GetEventsAsync(PrimaryCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "token"));

        await sut.SyncAsync(OwnerCalendarId, start, end);

        VerifyNothingLoggedContaining(logger, PrimaryCalendarId);
        VerifyNothingLoggedContaining(logger, PrimaryDisplayName);
        VerifySomethingLoggedContaining(logger, OwnerCalendarId.ToString());
    }

    [Fact]
    public async Task SyncAsync_WhenTheSyncTokenExpires_TheRestartWarningNamesTheCalendarById()
    {
        // The expired-token path is the one that used to read "Sync token expired for {CalendarName}."
        var (client, repo, logger, sut) = CreateSyncSut();
        var calendar = OwnerCalendar();
        var (start, end) = Window();

        repo.Setup(r => r.GetCalendarByIdAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendar]);
        repo.Setup(r => r.GetSyncStateAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = OwnerCalendarId, SyncToken = "stale" });
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(OwnerCalendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        client.Setup(c => c.GetEventsAsync(PrimaryCalendarId, null, null, "stale", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SyncTokenExpiredException());
        client.Setup(c => c.GetEventsAsync(PrimaryCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "fresh"));

        await sut.SyncAsync(OwnerCalendarId, start, end);

        VerifyLoggedAtLevelContaining(logger, LogLevel.Warning, "Sync token expired", OwnerCalendarId.ToString());
        VerifyNothingLoggedContaining(logger, PrimaryDisplayName);
    }

    [Fact]
    public async Task SyncAsync_WhenASeriesMasterCannotBeFetched_TheWarningNamesTheCalendarById()
    {
        // This warning used to carry {GoogleCalendarId} verbatim, and it fires on a transient Google
        // failure — the exact condition someone would go to Seq to investigate.
        var (client, repo, logger, sut) = CreateSyncSut();
        var calendar = OwnerCalendar();
        var (start, end) = Window();

        repo.Setup(r => r.GetCalendarByIdAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([calendar]);
        repo.Setup(r => r.GetSyncStateAsync(OwnerCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(OwnerCalendarId, start, end, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        repo.Setup(r => r.GetStoredRecurrenceRulesAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        client.Setup(c => c.GetEventsAsync(PrimaryCalendarId, start, end, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>
            {
                new() { GoogleEventId = "evt-1", Title = "Instance", GoogleRecurringEventId = "series-1" }
            }, "token"));
        client.Setup(c => c.GetSeriesMasterAsync(PrimaryCalendarId, "series-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeriesMaster?)null);

        await sut.SyncAsync(OwnerCalendarId, start, end);

        VerifyLoggedAtLevelContaining(logger, LogLevel.Warning, "Series master", OwnerCalendarId.ToString());
        VerifyNothingLoggedContaining(logger, PrimaryCalendarId);
    }

    // ── CalendarEventService ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_TheEventCreatedLogNamesTheCalendarByIdNotByItsGoogleId()
    {
        var (google, repo, logger, sut) = CreateEventSut();
        var calendar = OwnerCalendar();

        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([calendar]);
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        google.Setup(g => g.CreateEventAsync(PrimaryCalendarId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
            {
                e.GoogleEventId = "new-gid";
                return e;
            });

        await sut.CreateAsync(new CreateEventRequest(
            [OwnerCalendarId], "Title", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), false, null, null));

        VerifyNothingLoggedContaining(logger, PrimaryCalendarId);
        VerifyLoggedAtLevelContaining(logger, LogLevel.Information, "created on calendar", OwnerCalendarId.ToString());
    }

    // ── CalendarMigrationService ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureCorrectCalendarAsync_TheMigrationLogNamesBothCalendarsByIdNotByDisplayName()
    {
        var (google, repo, logger, sut) = CreateMigrationSut();
        var owner = MemberCalendar();
        var shared = SharedCalendar();
        var movingEvent = new CalendarEvent
        {
            Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            GoogleEventId = "gid-1",
            Title = "Trip",
            Description = "note",
            OwnerCalendarInfoId = MemberCalendarId
        };

        repo.Setup(r => r.GetCalendarByIdAsync(MemberCalendarId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(shared);
        repo.Setup(r => r.UpdateEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        google.Setup(g => g.CreateEventAsync(SharedCalendarGoogleId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) =>
            {
                e.GoogleEventId = "gid-2";
                return e;
            });
        google.Setup(g => g.DeleteEventAsync(It.IsAny<string>(), "gid-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var migrated = await sut.EnsureCorrectCalendarAsync(movingEvent, [owner, SharedCalendar()]);

        migrated.Should().BeTrue("the test only proves anything if the migration actually ran");
        VerifyNothingLoggedContaining(logger, MemberDisplayName);
        VerifyLoggedAtLevelContaining(logger, LogLevel.Information, "Migrating event", MemberCalendarId.ToString());
        VerifySomethingLoggedContaining(logger, SharedCalendarId.ToString());
    }

    // ── Shared assertions ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that no record at any level carries <paramref name="fragment"/>, in the rendered
    /// message or in any structured property — a property reaches Seq as its own field even when
    /// the template does not show it.
    /// </summary>
    private static void VerifyNothingLoggedContaining<T>(Mock<ILogger<T>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, fragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            $"'{fragment}' is PII under the logging standard and must never reach a log sink");

    private static void VerifySomethingLoggedContaining<T>(Mock<ILogger<T>> logger, string fragment) =>
        logger.Verify(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, fragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            $"redaction must leave '{fragment}' behind as the correlation key");

    private static void VerifyLoggedAtLevelContaining<T>(
        Mock<ILogger<T>> logger, LogLevel level, string messageFragment, string correlationFragment) =>
        logger.Verify(l => l.Log(
            level,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => Carries(v, messageFragment) && Carries(v, correlationFragment)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            $"the '{messageFragment}' line must still identify the calendar it is about");

    private static bool Carries(object? state, string fragment)
    {
        if (state?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return state is IReadOnlyList<KeyValuePair<string, object?>> values
            && values.Any(kv => kv.Value?.ToString()?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) Window()
    {
        var anchor = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        return (anchor.AddDays(-30), anchor.AddDays(30));
    }

    // ── SUT builders ─────────────────────────────────────────────────────────────────────────────

    private static (Mock<IGoogleCalendarClient> Client, Mock<ICalendarRepository> Repo,
        Mock<ILogger<CalendarSyncService>> Logger, CalendarSyncService Sut) CreateSyncSut()
    {
        var client = new Mock<IGoogleCalendarClient>();
        var repo = new Mock<ICalendarRepository>();
        var tagParser = new Mock<IMemberTagParser>();
        var logger = new Mock<ILogger<CalendarSyncService>>();
        var currentUser = new Mock<ICurrentUserService>();

        currentUser.SetupGet(u => u.UserId).Returns("u-1");
        tagParser.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new List<string>());

        var sut = new CalendarSyncService(
            client.Object,
            repo.Object,
            tagParser.Object,
            logger.Object,
            new Mock<ITokenStore>().Object,
            currentUser.Object,
            new Mock<ISyncFailureRepository>().Object,
            new Mock<IOutboundWriteHashCache>().Object);

        return (client, repo, logger, sut);
    }

    private static (Mock<IGoogleCalendarClient> Client, Mock<ICalendarRepository> Repo,
        Mock<ILogger<CalendarEventService>> Logger, CalendarEventService Sut) CreateEventSut()
    {
        var client = new Mock<IGoogleCalendarClient>();
        var repo = new Mock<ICalendarRepository>();
        var tagParser = new Mock<IMemberTagParser>();
        var logger = new Mock<ILogger<CalendarEventService>>();
        var currentUser = new Mock<ICurrentUserService>();

        currentUser.SetupGet(u => u.UserId).Returns("u-1");
        tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns((string d, IReadOnlyList<string> _) => d ?? string.Empty);
        tagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
            .Returns((string d) => d ?? string.Empty);

        var sut = new CalendarEventService(
            client.Object,
            repo.Object,
            new Mock<ICalendarMigrationService>().Object,
            tagParser.Object,
            new Mock<IOutboundWriteHashCache>().Object,
            currentUser.Object,
            new NodaTimeRecurrenceTimeZoneFactory(),
            logger.Object);

        return (client, repo, logger, sut);
    }

    private static (Mock<IGoogleCalendarClient> Client, Mock<ICalendarRepository> Repo,
        Mock<ILogger<CalendarMigrationService>> Logger, CalendarMigrationService Sut) CreateMigrationSut()
    {
        var client = new Mock<IGoogleCalendarClient>();
        var repo = new Mock<ICalendarRepository>();
        var logger = new Mock<ILogger<CalendarMigrationService>>();

        var sut = new CalendarMigrationService(
            client.Object,
            repo.Object,
            new MemberTagParser(),
            new Mock<IOutboundWriteHashCache>().Object,
            logger.Object);

        return (client, repo, logger, sut);
    }
}
