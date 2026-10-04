using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// The obsolete-calendar prune, and what it requires of the calendar-list fetch before it will act:
/// the fetch must have been answered after the local row was already stored, it must report itself
/// complete, and it must have named at least one calendar.
/// </summary>
/// <remarks>
/// <para>
/// This prune removes a calendar, and <c>CalendarRepository.RemoveCalendarAsync</c> removes every
/// event that calendar owns along with it, so the ordering of its two reads decides whether a
/// family loses one calendar's entire history. A row stored between the calendar-list fetch
/// returning and the local read is missing from that fetch for that reason alone — the fetch could
/// not have mentioned it.
/// </para>
/// <para>
/// The writer that inserts such a row is another whole-account sync: pass 1 of
/// <c>SyncAllAsync</c> is the only caller of <c>AddCalendarAsync</c>, and two of those passes can
/// run at once because <c>SyncController.TriggerSync</c> runs <c>SyncAllAsync</c> inline on the
/// HTTP request while <c>CalendarSyncWorker</c> runs it from the job queue, with nothing
/// serialising the two.
/// </para>
/// <para>
/// The race case below also pins the calendar that MUST still go, because an assertion that merely
/// said "nothing was removed" would pass against a prune that removes nothing at all. The
/// whole-fetch refusals cannot pin one, since they refuse the fetch outright: the incomplete case's
/// proof is the paired test on the identical arrangement (<c>ArrangeOneCalendarGone</c>) that
/// differs only in <c>IsComplete</c> and does remove, and the empty case additionally asserts the
/// log line that states why nothing went.
/// </para>
/// <para>
/// A complete-but-EMPTY calendar list is refused, unlike the empty-but-complete EVENT fetch the
/// full sync's tombstone diff acts on. The reason is specific to this entity: every Google account
/// has a primary calendar and the API lists it, so an account that holds none is unreachable —
/// whereas acting on it would take every calendar, every event on them and their
/// <c>SyncState</c>.
/// </para>
/// <para>
/// One case pins the other half of the earlier read-ordering fix: pass 1 keeps its own post-fetch
/// snapshot, so a calendar a concurrent sync inserted is not handed to <c>AddCalendarAsync</c> a
/// second time — the unique index on (GoogleCalendarId, UserId) would reject the insert and fail the
/// whole account's sync.
/// </para>
/// </remarks>
public class CalendarSyncServiceCalendarPruneTests
{
    private static readonly Guid KeptCalendarId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid ObsoleteCalendarId = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly Guid RacedCalendarId = Guid.Parse("c0000000-0000-0000-0000-000000000003");
    private static readonly Guid NewCalendarId = Guid.Parse("c0000000-0000-0000-0000-000000000004");

    private const string KeptGoogleId = "kept@group.calendar.google.com";
    private const string ObsoleteGoogleId = "deleted-in-google@group.calendar.google.com";
    private const string RacedGoogleId = "added-by-the-other-sync@group.calendar.google.com";
    private const string NewGoogleId = "brand-new@group.calendar.google.com";

    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncAllAsync_CalendarStoredBetweenTheFetchAndThePrune_IsNotRemoved()
    {
        var f = new Fixture();
        f.StoreCalendar(KeptCalendarId, KeptGoogleId, "Kept");
        f.StoreCalendar(ObsoleteCalendarId, ObsoleteGoogleId, "Gone");
        f.ArrangeGoogleCalendars(Fixture.GoogleCalendar(KeptCalendarId, KeptGoogleId, "Kept"));

        // The concurrent writer: pass 1 of another SyncAllAsync inserting a calendar this account
        // has just been given. Its insert lands AFTER the calendar list was answered, so Google's
        // answer could not have named it. Nothing but the ORDER of the local read protects it, and
        // removing it would take every event it owns with it.
        f.OnCalendarsFetched(() => f.StoreCalendar(RacedCalendarId, RacedGoogleId, "Raced"));

        await f.Sut.SyncAllAsync(WindowStart, WindowEnd);

        f.RemovedCalendarIds.Should().NotContain(RacedCalendarId);
        f.StoredGoogleCalendarIds.Should().Contain(RacedGoogleId);
        // ... while the prune did run, so the survival above is the read's ordering and not an
        // absent prune.
        f.RemovedCalendarIds.Should().Contain(ObsoleteCalendarId);
        f.StoredGoogleCalendarIds.Should().Contain(KeptGoogleId);
    }

    [Fact]
    public async Task SyncAllAsync_CalendarStoredBeforeTheFetchAndAbsentFromIt_IsRemoved()
    {
        var f = new Fixture();

        // An answer that names one of the two stored calendars: the other was deleted or unshared
        // in the Google Calendar app, and its absence is a real statement about it. This arrangement
        // is shared with the incomplete case below, which changes nothing but IsComplete — so this
        // test is also the proof that the calendar it refuses to remove there is there to remove.
        ArrangeOneCalendarGone(f, isComplete: true);

        await f.Sut.SyncAllAsync(WindowStart, WindowEnd);

        f.RemovedCalendarIds.Should().Contain(ObsoleteCalendarId);
        f.StoredGoogleCalendarIds.Should().BeEquivalentTo([KeptGoogleId]);
    }

    // ── When the calendar list is no statement of the account ─────────────────

    [Fact]
    public async Task SyncAllAsync_WhenTheCalendarListReportsItselfIncomplete_RemovesNothing()
    {
        var f = new Fixture();

        // GetCalendarsAsync took its calendarList page cap with a page token still outstanding, or
        // a page's body yielded no readable `items`. Either way it holds fewer calendars than Google
        // listed and cannot say which are missing, so no local calendar is judgeable against it.
        ArrangeOneCalendarGone(f, isComplete: false);

        await f.Sut.SyncAllAsync(WindowStart, WindowEnd);

        f.RemovedCalendarIds.Should().BeEmpty();
        f.StoredGoogleCalendarIds.Should().BeEquivalentTo([KeptGoogleId, ObsoleteGoogleId]);
        f.Logger.Records.Should().ContainSingle(r =>
            r.Level == LogLevel.Warning && r.Message.Contains("reported the fetch incomplete"));
        // Its control is SyncAllAsync_CalendarStoredBeforeTheFetchAndAbsentFromIt_IsRemoved, which
        // is this arrangement with IsComplete: true and does remove the obsolete calendar.
    }

    [Fact]
    public async Task SyncAllAsync_WhenTheCalendarListIsCompleteButEmpty_RemovesNothing()
    {
        var f = new Fixture();
        f.StoreCalendar(KeptCalendarId, KeptGoogleId, "Kept");
        f.StoreCalendar(ObsoleteCalendarId, ObsoleteGoogleId, "Gone");

        // A complete answer naming NO calendars. Every Google account has a primary calendar and the
        // API lists it in calendarList, so this is a state the application cannot legitimately
        // reach — while acting on it removes every calendar the family has, every event on them and
        // their SyncState, leaving the kiosk blank. This is the one place the prune declines to act
        // on what a complete answer says, and it is a deliberate departure from the full sync's
        // event-level diff, which DOES tombstone on an empty-and-complete fetch.
        f.ArrangeGoogleCalendarFetch(isComplete: true);

        await f.Sut.SyncAllAsync(WindowStart, WindowEnd);

        f.RemovedCalendarIds.Should().BeEmpty();
        f.StoredGoogleCalendarIds.Should().BeEquivalentTo([KeptGoogleId, ObsoleteGoogleId]);

        // Stated, not silent: a reader of Seq must be able to see why a sync removed nothing.
        f.Logger.Records.Should().ContainSingle(r =>
            r.Level == LogLevel.Warning && r.Message.Contains("complete but empty Google calendar list"));

        // And the sync still finished. Refusing the prune leaves the local calendars in place while
        // this run has synced none of them, so the first-login auto-designation below has no
        // calendar to pick — it must skip rather than throw out of the whole sync.
        f.Repo.Verify(r => r.MarkCalendarAsSharedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The arrangement shared by the completeness pair above so neither half can drift from the
    /// other: two stored calendars, and a fetch naming only one of them.
    /// </summary>
    private static void ArrangeOneCalendarGone(Fixture f, bool isComplete)
    {
        f.StoreCalendar(KeptCalendarId, KeptGoogleId, "Kept");
        f.StoreCalendar(ObsoleteCalendarId, ObsoleteGoogleId, "Gone");
        f.ArrangeGoogleCalendarFetch(isComplete, Fixture.GoogleCalendar(KeptCalendarId, KeptGoogleId, "Kept"));
    }

    [Fact]
    public async Task SyncAllAsync_CalendarStoredBetweenTheFetchAndPassOne_IsNotInsertedAgain()
    {
        var f = new Fixture();
        f.StoreCalendar(KeptCalendarId, KeptGoogleId, "Kept");
        f.ArrangeGoogleCalendars(
            Fixture.GoogleCalendar(KeptCalendarId, KeptGoogleId, "Kept"),
            Fixture.GoogleCalendar(RacedCalendarId, RacedGoogleId, "Raced"),
            Fixture.GoogleCalendar(NewCalendarId, NewGoogleId, "Brand new"));

        // A concurrent sync inserts the calendar Google has just listed to both of us, between the
        // fetch being answered and pass 1 reading the local rows.
        f.OnCalendarsFetched(() => f.StoreCalendar(RacedCalendarId, RacedGoogleId, "Raced"));

        await f.Sut.SyncAllAsync(WindowStart, WindowEnd);

        // Pass 1 must read the local rows AFTER the fetch, so it sees the raced insert and leaves
        // it alone: a second AddCalendarAsync for the same (GoogleCalendarId, UserId) violates the
        // unique index on CalendarInfo and SaveChangesAsync would take the whole account's sync
        // down with it. The calendar that really is new to this account is still inserted.
        f.AddedGoogleCalendarIds.Should().BeEquivalentTo([NewGoogleId]);
        f.RemovedCalendarIds.Should().BeEmpty();
    }

    /// <summary>
    /// Mocked dependencies over a stand-in for the stored calendars, so the repository's reads
    /// answer from live state: a prune that judges a calendar stored after the fetch shows up as a
    /// removed calendar rather than as an un-asserted mock call.
    /// </summary>
    private sealed class Fixture
    {
        public readonly Mock<IGoogleCalendarClient> Google = new();
        public readonly Mock<ICalendarRepository> Repo = new();
        // A real recording logger rather than a mock: the empty-list refusal is a stated decision,
        // and the statement is part of what this suite proves.
        public readonly RecordingLogger<CalendarSyncService> Logger = new();
        public readonly CalendarSyncService Sut;

        public readonly List<Guid> RemovedCalendarIds = [];
        public readonly List<string> AddedGoogleCalendarIds = [];

        private readonly List<CalendarInfo> _stored = [];
        private Action? _afterCalendarsFetched;

        public Fixture()
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.UserId).Returns("u-1");

            var tagParser = new Mock<IMemberTagParser>();
            tagParser.Setup(p => p.ParseMembers(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new List<string>());

            // The reads this suite is about, answering from live state — which is what makes the
            // order of the fetch and the local read observable at all.
            Repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _stored.ToList());
            Repo.Setup(r => r.GetCalendarByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => _stored.FirstOrDefault(c => c.Id == id));

            Repo.Setup(r => r.AddCalendarAsync(It.IsAny<CalendarInfo>(), It.IsAny<CancellationToken>()))
                .Callback((CalendarInfo c, CancellationToken _) =>
                {
                    AddedGoogleCalendarIds.Add(c.GoogleCalendarId);
                    _stored.Add(c);
                });
            Repo.Setup(r => r.RemoveCalendarAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, CancellationToken _) =>
                {
                    RemovedCalendarIds.Add(id);
                    _stored.RemoveAll(c => c.Id == id);
                });

            // A valid token and a reminder stamp on every calendar, so pass 2 takes the incremental
            // path: this suite is about the calendar list, and a full per-calendar sync would drag
            // the event-level tombstone diff in with it.
            Repo.Setup(r => r.GetSyncStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => new SyncState
                {
                    CalendarInfoId = id, SyncToken = "token-1", RemindersSyncedAt = WindowStart
                });
            Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

            Google.Setup(g => g.GetEventsAsync(
                    It.IsAny<string>(), It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GoogleEventFetch(new List<CalendarEvent>(), "next-token", IsComplete: true));

            Sut = new CalendarSyncService(
                Google.Object,
                Repo.Object,
                tagParser.Object,
                Logger,
                new Mock<ITokenStore>().Object,
                currentUser.Object,
                new Mock<ISyncFailureRepository>().Object,
                new Mock<IOutboundWriteHashCache>().Object);
        }

        public IReadOnlyList<string> StoredGoogleCalendarIds => _stored.Select(c => c.GoogleCalendarId).ToList();

        /// <summary>A calendar already in the local database.</summary>
        public void StoreCalendar(Guid id, string googleCalendarId, string displayName) =>
            _stored.Add(new CalendarInfo { Id = id, GoogleCalendarId = googleCalendarId, DisplayName = displayName });

        /// <summary>
        /// A calendar as the calendar-list fetch returns it. The display name matches the stored
        /// row's so <c>RefreshCalendarDefaultsAsync</c> finds nothing to adopt — name adoption has
        /// its own suite and is not this one's subject.
        /// </summary>
        public static CalendarInfo GoogleCalendar(Guid id, string googleCalendarId, string displayName) =>
            new() { Id = id, GoogleCalendarId = googleCalendarId, DisplayName = displayName };

        /// <summary>
        /// A COMPLETE calendar list, which is what GetCalendarsAsync returns when it reads every
        /// page Google offers — the normal answer, and the only one that authorises the prune.
        /// </summary>
        public void ArrangeGoogleCalendars(params CalendarInfo[] calendars) =>
            ArrangeGoogleCalendarFetch(isComplete: true, calendars);

        public void ArrangeGoogleCalendarFetch(bool isComplete, params CalendarInfo[] calendars) =>
            Google.Setup(g => g.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                .Callback(() => _afterCalendarsFetched?.Invoke())
                .ReturnsAsync(new GoogleCalendarFetch(calendars, isComplete));

        /// <summary>
        /// Runs when the calendar list is answered, to model a concurrent sync's insert landing
        /// after Google was asked what calendars exist.
        /// </summary>
        public void OnCalendarsFetched(Action action) => _afterCalendarsFetched = action;
    }
}
