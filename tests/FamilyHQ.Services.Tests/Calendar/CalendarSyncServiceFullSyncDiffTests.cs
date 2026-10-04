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
/// The full sync's tombstone diff, and the two things its verdict requires of the fetch: the fetch
/// must have been answered after the row was already stored, and it must report itself a complete
/// statement of what Google holds.
/// </summary>
/// <remarks>
/// <para>
/// This diff DELETES rows from the family's local database on the strength of Google's silence, so
/// the ordering of its two reads is load-bearing. A row stored between the fetch returning and the
/// candidate read is missing from the fetch for that reason alone — the fetch could not have
/// mentioned it — and the kiosk's own write path and another sync of the same calendar both insert
/// such rows while this runs.
/// </para>
/// <para>
/// Deleting one loses a real event permanently as far as syncing is concerned: an incremental sync
/// never re-sends an unchanged event, so only a later full sync would restore it. That is why the
/// race case below also pins the orphan that MUST still go — an assertion that merely says "nothing
/// was deleted" would pass against a service whose diff deletes nothing at all. The
/// incomplete-fetch case cannot pin one, since it refuses the whole fetch, so its proof is the
/// paired test on the identical arrangement (<c>ArrangeOneRowGone</c>) that differs only in
/// <c>IsComplete</c> and does delete.
/// </para>
/// <para>
/// An EMPTY-but-complete fetch is deliberately acted on here, unlike in
/// <c>CalendarEventService.PruneRowsAbsentFromWindowFetchAsync</c>, where it is refused because that
/// fetch is a read-back of a FamilyHQ write Google may not yet reflect. This sync makes no Google
/// write of its own, so it is reading nothing back and the answer is a statement about the window —
/// and refusing it would leave an emptied window's orphans to be removed by no code path at all. The
/// last case below pins that asymmetry.
/// </para>
/// </remarks>
public class CalendarSyncServiceFullSyncDiffTests
{
    private static readonly Guid CalendarId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ObsoleteRowId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid RacedRowId = Guid.Parse("e0000000-0000-0000-0000-000000000002");
    private static readonly Guid SurvivingRowId = Guid.Parse("e0000000-0000-0000-0000-000000000003");

    private const string GoogleCalId = "cal@group.calendar.google.com";

    // The sync window, fixed so the fetch stub matches on exact bounds.
    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Slot = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SyncAsync_FullSync_RowStoredBetweenTheFetchAndTheDiff_IsNotDeleted()
    {
        var f = new Fixture();
        var obsolete = f.StoreRow(ObsoleteRowId, "evt-gone", Slot);
        var kept = f.StoreRow(SurvivingRowId, "evt-kept", Slot.AddHours(1));
        f.ArrangeFullSyncFetch([f.Fetched("evt-kept", Slot.AddHours(1))]);

        // The concurrent writer: the kiosk's own write path, or another sync of this calendar
        // ingesting an event created on a phone. Its insert lands AFTER the fetch was answered, so
        // Google's answer could not have named it. Nothing but the ORDER of the candidate read
        // protects the row, and no incremental sync would bring it back.
        f.OnFetched(() => f.StoreRow(RacedRowId, "created-on-a-phone", Slot.AddHours(4)));

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        f.DeletedRowIds.Should().NotContain(RacedRowId);
        f.StoredGoogleEventIds.Should().Contain("created-on-a-phone");
        // ... while the diff did run, so the survival above is the read's ordering and not an
        // absent diff.
        f.DeletedRowIds.Should().Contain(obsolete.Id);
        f.StoredGoogleEventIds.Should().Contain(kept.GoogleEventId);
    }

    [Fact]
    public async Task SyncAsync_FullSync_RowStoredBeforeTheFetchAndAbsentFromIt_IsDeleted()
    {
        var f = new Fixture();

        // A complete answer that names one of the two stored rows: the other was deleted in the
        // Google Calendar app and its absence is a real statement about it. This arrangement is
        // shared with the incomplete case below, which changes nothing but IsComplete — so this
        // test is also the proof that the row it refuses to delete there is there to be deleted.
        ArrangeOneRowGone(f, isComplete: true);

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        f.DeletedRowIds.Should().Contain(ObsoleteRowId);
        f.StoredGoogleEventIds.Should().BeEquivalentTo(["evt-kept"]);
    }

    // ── When the fetch is not a complete statement of the window ──────────────

    [Fact]
    public async Task SyncAsync_FullSync_WhenTheFetchReportsItselfIncomplete_TombstonesNothing()
    {
        var f = new Fixture();

        // The fetch holds fewer events than Google has and cannot say which are missing (the
        // causes are on GoogleEventFetch.IsComplete; the client's own tests cover each). So a row it
        // failed to name may be a live event — and this diff is the only writer that would delete it
        // on the strength of that silence. The arrangement says IsComplete: false rather than
        // reproducing a cause, because what the diff may do about it is all that is under test.
        ArrangeOneRowGone(f, isComplete: false);

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        f.DeletedRowIds.Should().BeEmpty();
        f.StoredGoogleEventIds.Should().Contain("evt-gone");
        // Its control is SyncAsync_FullSync_RowStoredBeforeTheFetchAndAbsentFromIt_IsDeleted, which
        // is this arrangement with IsComplete: true and does delete the row.
    }

    [Fact]
    public async Task SyncAsync_FullSync_WhenTheFetchIsCompleteButEmpty_StillTombstonesEveryStoredRow()
    {
        var f = new Fixture();
        f.StoreRow(ObsoleteRowId, "evt-gone", Slot);
        f.StoreRow(SurvivingRowId, "evt-also-gone", Slot.AddHours(1));

        // Deliberately NOT refused here, which is where this diff parts company with
        // CalendarEventService's post-write prune (see CalendarEventServiceReconcilePruneTests,
        // where the same answer removes nothing). That prune reads back a window its own operation
        // has just written to; this sync makes no Google write of its own, so it is reading nothing
        // back and a complete answer naming nothing is a statement about the window. Refusing it
        // would leave an emptied window's orphans to be removed by no code path at all — this diff
        // is the one that clears them. A later change that "makes the two sites consistent" by
        // bolting an empty-fetch refusal on here turns this red.
        f.ArrangeFullSyncFetch([], isComplete: true);

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        f.DeletedRowIds.Should().BeEquivalentTo([ObsoleteRowId, SurvivingRowId]);
        f.StoredGoogleEventIds.Should().BeEmpty();
    }

    /// <summary>
    /// The arrangement shared by the completeness pair above so neither half can drift from the
    /// other: two stored rows, and a fetch naming only one of them.
    /// </summary>
    private static void ArrangeOneRowGone(Fixture f, bool isComplete)
    {
        f.StoreRow(ObsoleteRowId, "evt-gone", Slot);
        f.StoreRow(SurvivingRowId, "evt-kept", Slot.AddHours(1));
        f.ArrangeFullSyncFetch([f.Fetched("evt-kept", Slot.AddHours(1))], isComplete);
    }

    [Fact]
    public async Task SyncAsync_IncrementalSync_ReadsNoCandidatesAndDeletesNothing()
    {
        var f = new Fixture(syncToken: "token-1", remindersSyncedAt: WindowStart);
        f.StoreRow(ObsoleteRowId, "evt-not-in-this-page", Slot);

        // An incremental fetch is a statement about what CHANGED, never about what the window
        // holds, so absence from it means nothing and the diff is skipped entirely.
        f.ArrangeIncrementalFetch("token-1", [f.Fetched("evt-changed", Slot.AddHours(1))]);

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        f.DeletedRowIds.Should().BeEmpty();
        f.StoredGoogleEventIds.Should().Contain("evt-not-in-this-page");
        // The candidate read is the diff's query and must not be paid for on the common path.
        f.Repo.Verify(r => r.GetEventsByOwnerCalendarAsync(
            It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_FullSync_ReadsDiffCandidatesForTheSyncedCalendarAndWindowOnce()
    {
        var f = new Fixture();
        f.StoreRow(ObsoleteRowId, "evt-gone", Slot);
        f.ArrangeFullSyncFetch([f.Fetched("evt-kept", Slot.AddHours(1))]);

        await f.Sut.SyncAsync(CalendarId, WindowStart, WindowEnd);

        // The bounds are the window this sync was asked for and the calendar it is syncing — and
        // exactly one read, so hoisting it above the fetch did not leave the old one behind.
        f.Repo.Verify(r => r.GetEventsByOwnerCalendarAsync(
            CalendarId, WindowStart, WindowEnd, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.GetEventsByOwnerCalendarAsync(
            It.Is<Guid>(id => id != CalendarId), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Mocked dependencies over a stand-in for the stored rows, so the repository's reads answer
    /// from live state: a diff that judges a row stored after the fetch shows up as a missing row
    /// rather than as an un-asserted mock call.
    /// </summary>
    private sealed class Fixture
    {
        public readonly Mock<IGoogleCalendarClient> Google = new();
        public readonly Mock<ICalendarRepository> Repo = new();
        public readonly CalendarSyncService Sut;

        public readonly List<Guid> DeletedRowIds = [];

        private readonly List<CalendarEvent> _stored = [];
        private Action? _afterFetch;

        private readonly CalendarInfo _calendar = new()
        {
            Id = CalendarId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice"
        };

        public Fixture(string? syncToken = null, DateTimeOffset? remindersSyncedAt = null)
        {
            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.UserId).Returns("u-1");

            var tagParser = new Mock<IMemberTagParser>();
            tagParser.Setup(p => p.ParseMembers(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new List<string>());

            Repo.Setup(r => r.GetCalendarByIdAsync(CalendarId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_calendar);
            Repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([_calendar]);
            Repo.Setup(r => r.GetSyncStateAsync(CalendarId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SyncState
                {
                    CalendarInfoId = CalendarId,
                    SyncToken = syncToken,
                    // Null here forces one full sync of its own (the reminder backfill), so an
                    // incremental case has to supply it.
                    RemindersSyncedAt = remindersSyncedAt
                });
            Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

            // The diff's candidate read, with the repository's documented predicate: one calendar,
            // matching on OVERLAP. Deliberately the wide predicate — narrowing it here would move
            // the behaviour under test into the test double. It answers from live state, which is
            // what makes the ordering of the two reads observable at all.
            Repo.Setup(r => r.GetEventsByOwnerCalendarAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid calendarId, DateTimeOffset start, DateTimeOffset end, CancellationToken _) =>
                    _stored.Where(e => e.OwnerCalendarInfoId == calendarId && e.Start < end && e.End > start).ToList());

            Repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string googleEventId, CancellationToken _) =>
                    _stored.FirstOrDefault(e => e.GoogleEventId == googleEventId));
            Repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
                .Callback((CalendarEvent e, CancellationToken _) => _stored.Add(e));
            Repo.Setup(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, CancellationToken _) =>
                {
                    DeletedRowIds.Add(id);
                    _stored.RemoveAll(e => e.Id == id);
                });

            Sut = new CalendarSyncService(
                Google.Object,
                Repo.Object,
                tagParser.Object,
                new Mock<ILogger<CalendarSyncService>>().Object,
                new Mock<ITokenStore>().Object,
                currentUser.Object,
                new Mock<ISyncFailureRepository>().Object,
                new Mock<IOutboundWriteHashCache>().Object);
        }

        public IReadOnlyList<string> StoredGoogleEventIds => _stored.Select(e => e.GoogleEventId).ToList();

        /// <summary>A row stored on the calendar being synced, inside its window.</summary>
        public CalendarEvent StoreRow(Guid id, string googleEventId, DateTimeOffset start)
        {
            var row = new CalendarEvent
            {
                Id = id,
                GoogleEventId = googleEventId,
                Title = "Something",
                Start = start,
                End = start.AddHours(1),
                OwnerCalendarInfoId = CalendarId,
                Members = [_calendar]
            };
            _stored.Add(row);
            return row;
        }

        /// <summary>An event as the fetch returns it: pass 1, so non-recurring and RRULE-less.</summary>
        public CalendarEvent Fetched(string googleEventId, DateTimeOffset start) => new()
        {
            GoogleEventId = googleEventId,
            Title = "Something",
            Start = start,
            End = start.AddHours(1)
        };

        /// <summary>A windowed fetch, which is the only kind a full sync makes.</summary>
        /// <param name="isComplete">
        /// What the client reports about the fetch. True on every case that is not about
        /// completeness, because that is what GetEventsAsync returns when it reads every page
        /// Google offers — which is the normal answer and the only one that authorises the diff.
        /// </param>
        public void ArrangeFullSyncFetch(IReadOnlyList<CalendarEvent> events, bool isComplete = true) =>
            Google.Setup(g => g.GetEventsAsync(
                    GoogleCalId, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
                .Callback(() => _afterFetch?.Invoke())
                .ReturnsAsync(new GoogleEventFetch(events, "next-token", isComplete));

        /// <summary>A token fetch, which carries no time range — Google rejects the combination.</summary>
        public void ArrangeIncrementalFetch(string syncToken, IReadOnlyList<CalendarEvent> events) =>
            Google.Setup(g => g.GetEventsAsync(
                    GoogleCalId, null, null, syncToken, It.IsAny<CancellationToken>()))
                .Callback(() => _afterFetch?.Invoke())
                .ReturnsAsync(new GoogleEventFetch(events, "next-token", IsComplete: true));

        /// <summary>Runs when the fetch is answered, to model a concurrent writer's timing.</summary>
        public void OnFetched(Action action) => _afterFetch = action;
    }
}
