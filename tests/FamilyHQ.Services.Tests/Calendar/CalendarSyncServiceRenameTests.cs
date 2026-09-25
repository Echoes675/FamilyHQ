using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

// FHQ-211: a calendar renamed (or recoloured) in the Google Calendar app must reach FamilyHQ.
// Google is the system of record and RefreshCalendarDefaultsAsync used to adopt only IanaTimeZone
// and DefaultReminders, so a rename made on a phone was dropped on the floor forever — the row kept
// the name it was first inserted with. That stale name is load-bearing, not cosmetic: member
// resolution matches calendar DISPLAY NAMES in an event's description, so after a rename a
// description naming the new name stopped resolving and one naming the old name still did.
//
// There is no local rename to protect: CalendarSettingsRequest carries only IsVisible/IsShared, so
// FamilyHQ has never let anyone rename or recolour a calendar and Google's value is unambiguously
// authoritative.
public class CalendarSyncServiceRenameTests
{
    private static readonly DateTimeOffset WindowStart = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd   = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid MemberCalendarId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private static readonly Guid SharedCalendarId = Guid.Parse("bbbbbbbb-2222-2222-2222-bbbbbbbbbbbb");

    private const string MemberGoogleCalendarId = "member@group.calendar.google.com";
    private const string SharedGoogleCalendarId = "shared@group.calendar.google.com";

    // ── DisplayName ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Rob", "Work", "Rob", true)]     // renamed in Google → adopted, because Google is the record
    [InlineData(null, "Work", "Work", false)]    // absent must never blank a stored name
    [InlineData("", "Work", "Work", false)]      // an empty summary is absent, not a new name
    [InlineData("   ", "Work", "Work", false)]   // whitespace likewise
    [InlineData("Work", "Work", "Work", false)]  // idempotent: no write, no churn
    public async Task SyncAllAsync_ExistingCalendar_AdoptsGooglesDisplayNameWithoutEverBlankingIt(
        string? googleName, string storedName, string expectedName, bool expectsWrite)
    {
        var (localCalendar, repo, _) = await RunCalendarDefaultsRefreshAsync(
            googleName: googleName, storedName: storedName);

        localCalendar.DisplayName.Should().Be(expectedName);
        repo.Verify(
            r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()),
            expectsWrite ? Times.Once() : Times.Never());
    }

    // ── Color ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("#33b679", "#7986cb", "#33b679", true)]   // recoloured in Google → adopted
    [InlineData("#33b679", null, "#33b679", true)]        // never observed before → adopted
    [InlineData(null, "#7986cb", "#7986cb", false)]       // absent must never blank a stored colour
    [InlineData("", "#7986cb", "#7986cb", false)]         // empty is absent, not a new colour
    [InlineData("#7986cb", "#7986cb", "#7986cb", false)]  // idempotent: no write, no churn
    public async Task SyncAllAsync_ExistingCalendar_AdoptsGooglesColourWithoutEverBlankingIt(
        string? googleColour, string? storedColour, string? expectedColour, bool expectsWrite)
    {
        var (localCalendar, repo, _) = await RunCalendarDefaultsRefreshAsync(
            googleColour: googleColour, storedColour: storedColour);

        localCalendar.Color.Should().Be(expectedColour);
        repo.Verify(
            r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()),
            expectsWrite ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task SyncAllAsync_ExistingCalendar_RenameIsAdoptedFromSummaryOverrideShapedValue()
    {
        // GoogleCalendarClient.GetCalendarsAsync maps DisplayName from `summaryOverride ?? summary`,
        // so by the time the refresh sees it the per-account name the user actually sees is already
        // in DisplayName. The refresh must therefore compare against DisplayName — the same field
        // the add path stores — and nothing else.
        var (localCalendar, repo, _) = await RunCalendarDefaultsRefreshAsync(
            googleName: "James", storedName: "Personal");

        localCalendar.DisplayName.Should().Be("James");
        repo.Verify(r => r.UpdateCalendarAsync(localCalendar, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddCalendarAsync(It.IsAny<CalendarInfo>(), It.IsAny<CancellationToken>()), Times.Never,
            "a renamed calendar is the SAME calendar — its GoogleCalendarId has not changed");
    }

    [Fact]
    public async Task SyncAllAsync_ExistingCalendar_RenameLogDoesNotLeakTheCalendarName()
    {
        // A calendar's display name is a family member's name (and the account's email address for a
        // primary calendar) — PII that must never reach Seq (FHQ-166). The calendar's own id says
        // the same thing.
        var logger = new Mock<ILogger<CalendarSyncService>>();

        await RunCalendarDefaultsRefreshAsync(googleName: "Rob", storedName: "Work", logger: logger);

        logger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) =>
                v.ToString()!.Contains(MemberCalendarId.ToString())
                && !v.ToString()!.Contains("Rob")
                && !v.ToString()!.Contains("Work")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    // ── The change count: a rename IS user-visible, unlike the zone ─────────

    [Theory]
    [InlineData("Rob", null, null, 1)]        // renamed  → rendered → material, so the kiosk refreshes
    [InlineData(null, "#33b679", null, 1)]    // recoloured → rendered → material
    [InlineData(null, null, "Europe/London", 0)]  // zone only → not rendered → still bookkeeping
    [InlineData(null, null, null, 0)]         // nothing changed → nothing written, nothing counted
    public async Task SyncAllAsync_ARenameOrRecolourCountsAsAMaterialChange_AZoneChangeDoesNot(
        string? googleName, string? googleColour, string? googleZone, int expectedChangedCount)
    {
        // CalendarSyncWorker broadcasts "EventsUpdated" only when SyncResult.HadChanges (FHQ-44), and
        // the kiosk's handler refetches the calendar list — so the change count is what puts a new
        // calendar name and colour on screen. A calendar's default zone and default reminders are
        // never rendered, so they stay bookkeeping; a name and a colour are rendered in every Agenda
        // column header and event chip, so leaving them uncounted would fix the database and leave
        // the kiosk showing a name the family no longer uses until something unrelated changed.
        var (_, _, result) = await RunCalendarDefaultsRefreshAsync(
            googleName: googleName ?? "Work",
            storedName: "Work",
            googleColour: googleColour,
            storedColour: null,
            googleZone: googleZone,
            storedZone: null,
            savedRowCount: 1);

        result.ChangedCount.Should().Be(expectedChangedCount);
    }

    // ── The consequence: member resolution follows the rename ───────────────

    [Fact]
    public async Task SyncAllAsync_AfterARename_ATagNamingTheNewNameResolvesToThatCalendar()
    {
        var addedEvent = await SyncTaggedEventAfterRenameAsync(tagName: "Rob");

        addedEvent.Should().NotBeNull();
        addedEvent!.Members.Should().ContainSingle()
            .Which.Id.Should().Be(MemberCalendarId);
    }

    [Fact]
    public async Task SyncAllAsync_AfterARename_ATagNamingTheOldNameNoLongerResolves()
    {
        var addedEvent = await SyncTaggedEventAfterRenameAsync(tagName: "Work");

        addedEvent.Should().NotBeNull();
        addedEvent!.Members.Should().BeEmpty(
            "the family no longer calls that calendar Work, so a description naming it must not resolve");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full sync over one existing calendar whose stored name/colour and Google's reported
    /// name/colour are supplied independently, and returns the local row (mutated in place, as the
    /// refresh does) together with the repository mock so the caller can assert whether it wrote.
    /// </summary>
    private static async Task<(CalendarInfo localCalendar, Mock<ICalendarRepository> repo, SyncResult result)> RunCalendarDefaultsRefreshAsync(
        string? googleName = "Calendar",
        string? storedName = "Calendar",
        string? googleColour = null,
        string? storedColour = null,
        string? googleZone = null,
        string? storedZone = null,
        int savedRowCount = 0,
        Mock<ILogger<CalendarSyncService>>? logger = null)
    {
        var googleCalendar = new CalendarInfo
        {
            Id = MemberCalendarId, GoogleCalendarId = MemberGoogleCalendarId,
            DisplayName = googleName!, Color = googleColour, IanaTimeZone = googleZone
        };
        var localCalendar = new CalendarInfo
        {
            Id = MemberCalendarId, GoogleCalendarId = MemberGoogleCalendarId,
            DisplayName = storedName!, Color = storedColour, IanaTimeZone = storedZone
        };

        var (client, repo, _, sut) = CreateSut(logger);

        // One row written per UpdateCalendarAsync + SaveChangesAsync pair, as Postgres would report.
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(savedRowCount);

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { googleCalendar });
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { localCalendar });
        repo.Setup(r => r.GetCalendarByIdAsync(MemberCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localCalendar);
        repo.Setup(r => r.GetSyncStateAsync(MemberCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        client.Setup(c => c.GetEventsAsync(
                MemberGoogleCalendarId, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "sync-token"));
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(
                MemberCalendarId, WindowStart, WindowEnd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());

        var result = await sut.SyncAllAsync(WindowStart, WindowEnd);

        return (localCalendar, repo, result);
    }

    /// <summary>
    /// Renames a member calendar in Google from <c>Work</c> to <c>Rob</c> during one full sync, and
    /// returns the event the sync persisted for a shared-calendar event whose description carries
    /// <c>[members: <paramref name="tagName"/>]</c>. The real <see cref="MemberTagParser"/> is used:
    /// the point of the test is what the tag resolves to once the rename has been adopted, and a
    /// mocked parser would assert nothing about that.
    /// </summary>
    private static async Task<CalendarEvent?> SyncTaggedEventAfterRenameAsync(string tagName)
    {
        // Google's current state: the member calendar is now called Rob.
        var googleMember = new CalendarInfo
        {
            Id = MemberCalendarId, GoogleCalendarId = MemberGoogleCalendarId, DisplayName = "Rob"
        };
        var googleShared = new CalendarInfo
        {
            Id = SharedCalendarId, GoogleCalendarId = SharedGoogleCalendarId, DisplayName = "Household"
        };

        // What FamilyHQ stored before the rename.
        var localMember = new CalendarInfo
        {
            Id = MemberCalendarId, GoogleCalendarId = MemberGoogleCalendarId, DisplayName = "Work",
            IsShared = false
        };
        var localShared = new CalendarInfo
        {
            Id = SharedCalendarId, GoogleCalendarId = SharedGoogleCalendarId, DisplayName = "Household",
            IsShared = true
        };

        var (client, repo, _, sut) = CreateSut(logger: null, tagParser: new MemberTagParser());

        client.Setup(c => c.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { googleMember, googleShared });
        // The same instances are returned on every read, so the refresh's in-place mutation is
        // visible to pass 2 exactly as a committed row would be.
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarInfo> { localMember, localShared });
        repo.Setup(r => r.GetCalendarByIdAsync(MemberCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localMember);
        repo.Setup(r => r.GetCalendarByIdAsync(SharedCalendarId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localShared);
        repo.Setup(r => r.GetSyncStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        repo.Setup(r => r.GetEventsByOwnerCalendarAsync(
                It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEvent>());
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        // The member calendar itself has no events; the tagged event lives on the shared calendar,
        // so the memberless-event fallback (which adds the OWNING calendar when it is not shared)
        // cannot mask what the tag resolved to.
        client.Setup(c => c.GetEventsAsync(
                MemberGoogleCalendarId, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>(), "member-token"));
        client.Setup(c => c.GetEventsAsync(
                SharedGoogleCalendarId, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent>
            {
                new()
                {
                    GoogleEventId = "tagged-evt",
                    Title         = "Swimming",
                    Description   = $"Swimming lesson [members: {tagName}]",
                    Start         = WindowStart.AddDays(3),
                    End           = WindowStart.AddDays(3).AddHours(1)
                }
            }, "shared-token"));

        CalendarEvent? added = null;
        repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<CalendarEvent, CancellationToken>((e, _) => added = e);

        await sut.SyncAllAsync(WindowStart, WindowEnd);

        return added;
    }

    // Mirrors CalendarSyncServiceTests.CreateSutWithAllDeps: the same mocked dependency set, wired
    // fresh per call so tests never share mutable mock state.
    private static (Mock<IGoogleCalendarClient> client, Mock<ICalendarRepository> repo,
        Mock<ILogger<CalendarSyncService>> logger, CalendarSyncService sut) CreateSut(
        Mock<ILogger<CalendarSyncService>>? logger = null, IMemberTagParser? tagParser = null)
    {
        var clientMock          = new Mock<IGoogleCalendarClient>();
        var repoMock            = new Mock<ICalendarRepository>();
        var loggerMock          = logger ?? new Mock<ILogger<CalendarSyncService>>();
        var tokenStoreMock      = new Mock<ITokenStore>();
        var currentUserMock     = new Mock<ICurrentUserService>();
        var syncFailureRepoMock = new Mock<ISyncFailureRepository>();
        var outboundCacheMock   = new Mock<IOutboundWriteHashCache>();

        currentUserMock.SetupGet(c => c.UserId).Returns("test-user");

        if (tagParser is null)
        {
            var tagParserMock = new Mock<IMemberTagParser>();
            tagParserMock
                .Setup(p => p.ParseMembers(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new List<string>());
            tagParser = tagParserMock.Object;
        }

        var sut = new CalendarSyncService(
            clientMock.Object,
            repoMock.Object,
            tagParser,
            loggerMock.Object,
            tokenStoreMock.Object,
            currentUserMock.Object,
            syncFailureRepoMock.Object,
            outboundCacheMock.Object);

        return (clientMock, repoMock, loggerMock, sut);
    }
}
