using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-18.4 — series-level 1↔N calendar migration (spec §10.1.3). When an "All events" edit crosses
/// the single/shared membership boundary, the whole series is re-created on the target calendar with
/// a normalised members tag, every local instance is repointed to the new series id, and the old
/// series is deleted from Google. An outbound hash is recorded for every touched instance.
/// </summary>
public class CalendarMigrationServiceSeriesTests
{
    private static readonly Guid AliceCalId  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BobCalId    = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SharedCalId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const string SeriesId = "old-series-id";
    private const string MasterEchoedHash = "new-master-echoed-hash";
    private const string AliceGoogleCal  = "alice@";
    private const string SharedGoogleCal = "shared@";

    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd   = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static CalendarInfo Alice => new() { Id = AliceCalId, GoogleCalendarId = AliceGoogleCal, DisplayName = "Alice", IsShared = false };
    private static CalendarInfo Bob => new() { Id = BobCalId, GoogleCalendarId = "bob@", DisplayName = "Bob", IsShared = false };
    private static CalendarInfo Shared => new() { Id = SharedCalId, GoogleCalendarId = SharedGoogleCal, DisplayName = "Family", IsShared = true };

    [Fact]
    public async Task EnsureCorrectCalendarForSeries_SingleToMulti_MovesSeriesRepointsInstancesDeletesOld()
    {
        var (google, repo, cache, sut) = CreateSut();

        // Series currently lives on Alice's individual calendar with two instances.
        var inst1 = SeriesInstance("inst-1", WindowStart.AddDays(7), AliceCalId);
        var inst2 = SeriesInstance("inst-2", WindowStart.AddDays(14), AliceCalId);
        repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([inst1, inst2]);
        repo.Setup(r => r.GetCalendarByIdAsync(AliceCalId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Shared);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Alice, Bob, Shared]);
        repo.Setup(r => r.GetSyncStateAsync(SharedCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = SharedCalId, SyncWindowStart = WindowStart, SyncWindowEnd = WindowEnd });
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        // Insert on the shared calendar returns the new series id.
        google.Setup(g => g.CreateRecurringEventAsync(SharedGoogleCal, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-series-id"; return e; });

        // Google copies the new master's content-hash onto every expanded instance; GetEventsAsync
        // surfaces that echoed value on ContentHash. The reconcile must record THAT exact value.
        var new1 = NewInstance("new-1", WindowStart.AddDays(7)); new1.ContentHash = MasterEchoedHash;
        var new2 = NewInstance("new-2", WindowStart.AddDays(14)); new2.ContentHash = MasterEchoedHash;
        google.Setup(g => g.GetEventsAsync(SharedGoogleCal, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { new1, new2 }, (string?)null));

        var migrated = await sut.EnsureCorrectCalendarForSeriesAsync(SeriesId, [Alice, Bob]);

        migrated.Should().BeTrue();
        // Inserted the new series on the shared calendar with a recurrence rule.
        google.Verify(g => g.CreateRecurringEventAsync(SharedGoogleCal, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.Is<string>(r => r.Contains("FREQ=")), It.IsAny<CancellationToken>()), Times.Once);
        // Deleted the old series from Alice's calendar.
        google.Verify(g => g.DeleteEventAsync(AliceGoogleCal, SeriesId, It.IsAny<CancellationToken>()), Times.Once);
        // Old local rows removed.
        repo.Verify(r => r.DeleteEventAsync(inst1.Id, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.DeleteEventAsync(inst2.Id, It.IsAny<CancellationToken>()), Times.Once);
        // New instances persisted pointing at the new series id and shared calendar.
        repo.Verify(r => r.AddEventAsync(It.Is<CalendarEvent>(e => e.GoogleRecurringEventId == "new-series-id" && e.OwnerCalendarInfoId == SharedCalId), It.IsAny<CancellationToken>()), Times.Exactly(2));
        // Hash recorded for the master insert plus each reconciled instance. The instances record
        // the EXACT echoed master hash Google copied onto them — not a per-instance recompute.
        cache.Verify(c => c.Record("new-series-id", It.IsAny<string>()), Times.Once);
        cache.Verify(c => c.Record("new-1", MasterEchoedHash), Times.Once);
        cache.Verify(c => c.Record("new-2", MasterEchoedHash), Times.Once);
    }

    [Fact]
    public async Task EnsureCorrectCalendarForSeries_ExplicitTagNamingSharedCalendar_RetainsThatMember()
    {
        // FHQ-47 (Gap 2): a member calendar that is TRANSIENTLY marked IsShared (the first-login
        // auto-designation window) must NOT be dropped from a series instance whose description
        // carries an explicit "[members: ...]" tag naming it. The reconcile resolves the explicit
        // tag against ALL calendars (authoritative), so the tagged member survives.
        var (google, repo, _, sut) = CreateSut();

        // Bob's individual calendar is currently (transiently) flagged shared.
        var transientlySharedBob = new CalendarInfo { Id = BobCalId, GoogleCalendarId = "bob@", DisplayName = "Bob", IsShared = true };

        var inst1 = SeriesInstance("inst-1", WindowStart.AddDays(7), AliceCalId);
        repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([inst1]);
        repo.Setup(r => r.GetCalendarByIdAsync(AliceCalId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Shared);
        repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Alice, transientlySharedBob, Shared]);
        repo.Setup(r => r.GetSyncStateAsync(SharedCalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncState { CalendarInfoId = SharedCalId, SyncWindowStart = WindowStart, SyncWindowEnd = WindowEnd });
        repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        google.Setup(g => g.CreateRecurringEventAsync(SharedGoogleCal, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-series-id"; return e; });

        // The migrated instance's description explicitly tags Alice AND Bob (Bob now shared).
        var newInst = NewInstance("new-1", WindowStart.AddDays(7));
        newInst.Description = "Body\n[members: Alice, Bob]";
        google.Setup(g => g.GetEventsAsync(SharedGoogleCal, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<CalendarEvent> { newInst }, (string?)null));

        // Migrate the series to the shared calendar (Alice + transiently-shared Bob → multi-member).
        var migrated = await sut.EnsureCorrectCalendarForSeriesAsync(SeriesId, [Alice, transientlySharedBob]);

        migrated.Should().BeTrue();
        // The reconciled instance retains BOTH tagged members — Bob is NOT dropped despite IsShared.
        repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "new-1"
                && e.Members.Any(m => m.DisplayName == "Alice")
                && e.Members.Any(m => m.DisplayName == "Bob")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureCorrectCalendarForSeries_AlreadyOnCorrectCalendar_NoMigration()
    {
        var (google, repo, _, sut) = CreateSut();

        var inst1 = SeriesInstance("inst-1", WindowStart.AddDays(7), AliceCalId);
        repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([inst1]);
        repo.Setup(r => r.GetCalendarByIdAsync(AliceCalId, It.IsAny<CancellationToken>())).ReturnsAsync(Alice);
        repo.Setup(r => r.GetSharedCalendarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Shared);

        // Single member, already on individual calendar → no migration.
        var migrated = await sut.EnsureCorrectCalendarForSeriesAsync(SeriesId, [Alice]);

        migrated.Should().BeFalse();
        google.Verify(g => g.CreateRecurringEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CalendarEvent SeriesInstance(string googleEventId, DateTimeOffset start, Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        GoogleEventId = googleEventId,
        Title = "Yoga",
        Start = start,
        End = start.AddHours(1),
        Description = "Body\n[members: Alice]",
        OwnerCalendarInfoId = ownerId,
        GoogleRecurringEventId = SeriesId,
        RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=MO"
    };

    private static CalendarEvent NewInstance(string googleEventId, DateTimeOffset start) => new()
    {
        GoogleEventId = googleEventId,
        Title = "Yoga",
        Start = start,
        End = start.AddHours(1),
        Description = "Body\n[members: Alice, Bob]",
        GoogleRecurringEventId = "new-series-id",
        RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=MO"
    };

    private static (Mock<IGoogleCalendarClient> google, Mock<ICalendarRepository> repo, Mock<IOutboundWriteHashCache> cache, CalendarMigrationService sut) CreateSut()
    {
        var google = new Mock<IGoogleCalendarClient>();
        var repo   = new Mock<ICalendarRepository>();
        var cache  = new Mock<IOutboundWriteHashCache>();
        var logger = new Mock<ILogger<CalendarMigrationService>>();
        var sut    = new CalendarMigrationService(google.Object, repo.Object, new MemberTagParser(), cache.Object, logger.Object);
        return (google, repo, cache, sut);
    }
}
