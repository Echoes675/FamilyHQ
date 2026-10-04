using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// The post-write reconcile's prune: a row the owner calendar's window fetch no longer lists is
/// removed, and a row the fetch says nothing about is not.
/// </summary>
/// <remarks>
/// <para>
/// The prune exists because an exception's Google id is <c>{masterId}_{originalStartStamp}</c>, so
/// an all-in-series timing change renames every instance and strands every row carrying an old id.
/// The family then sees two tiles on one slot — the stale row cannot be deleted from the kiosk,
/// because Google no longer holds the event the row names.
/// </para>
/// <para>
/// Every test here is about a DELETION of a family's local row, so the negative cases carry the
/// weight: each one puts a row the fetch did not mention somewhere the fetch proves nothing about,
/// and asserts it survives a reconcile that does prune in the same breath. A test that only
/// asserted "nothing was deleted" would pass against a service with no prune at all, so each
/// negative case also pins the orphan that MUST go.
/// </para>
/// </remarks>
public class CalendarEventServiceReconcilePruneTests
{
    private static readonly Guid AliceCalId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BobCalId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedCalId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static readonly Guid EditedRowId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondRowId = Guid.Parse("e0000000-0000-0000-0000-000000000002");
    private static readonly Guid StrayRowId = Guid.Parse("e0000000-0000-0000-0000-000000000003");
    private static readonly Guid MasterRowId = Guid.Parse("e0000000-0000-0000-0000-000000000004");

    private const string SeriesId = "series-master-id";
    private const string GoogleCalId = "alice@google.com";
    private const string Rrule = "RRULE:FREQ=WEEKLY;BYDAY=SU";

    // The window the stored SyncState holds, and therefore the one the reconcile fetches.
    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    // The edited occurrence, and the slot it moves to when the all-in-series edit shifts the anchor.
    private static readonly DateTimeOffset OldSlot = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NewSlot = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);

    private static string InstanceId(DateTimeOffset slot) =>
        $"{SeriesId}_{slot.UtcDateTime:yyyyMMdd'T'HHmmss}Z";

    // ── The orphan the prune exists for ───────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeriesAnchorMoves_RemovesTheRenamedRowsAndKeepsTheNewOnes()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        f.StoreInstance(SecondRowId, InstanceId(OldSlot.AddDays(7)), OldSlot.AddDays(7));

        // Google renames every instance when the anchor moves: same series, new stamps.
        f.ArrangeWindowFetch([
            f.FetchedInstance(InstanceId(NewSlot), NewSlot),
            f.FetchedInstance(InstanceId(NewSlot.AddDays(7)), NewSlot.AddDays(7))
        ]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        // The family's window holds the new occurrences and nothing else — no second tile on the
        // slot the series used to occupy.
        f.StoredGoogleEventIds.Should().BeEquivalentTo([InstanceId(NewSlot), InstanceId(NewSlot.AddDays(7))]);
        f.DeletedRowIds.Should().BeEquivalentTo([EditedRowId, SecondRowId]);
    }

    // ── What the fetch proves nothing about ───────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RowStartingAfterTheFetchedWindow_SurvivesThePrune()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);

        // Google's timeMax is an exclusive upper bound on an event's start, so a row starting at or
        // after the window's end was never covered by the fetch and its absence means nothing.
        var beyond = f.StoreUnrelated(StrayRowId, "far-future-event", WindowEnd.AddDays(3));

        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(beyond.Id);
        f.StoredGoogleEventIds.Should().Contain("far-future-event");
        // ... while the reconcile did prune, so the survival above is the window bound and not an
        // absent prune.
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RowStartingBeforeTheFetchedWindow_SurvivesThePrune()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);

        // A row that starts before the window but runs into it IS something Google's overlap filter
        // would have returned — on the strength of its stored End, which is the one value a stale
        // row may have wrong. Left alone deliberately; the next full sync is what removes it.
        var straddling = f.StoreUnrelated(StrayRowId, "long-running-event", WindowStart.AddDays(-2));
        straddling.End = WindowStart.AddDays(2);

        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(straddling.Id);
        f.StoredGoogleEventIds.Should().Contain("long-running-event");
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RowInsideTheSecondTheWindowEndDiscards_SurvivesThePrune()
    {
        // The client formats timeMax to whole seconds, so a stored window end carrying a fraction of
        // a second — which it does, being the wall-clock instant of the full sync that wrote it —
        // asks Google for less than the local predicate would otherwise allow.
        var windowEnd = WindowEnd.AddTicks(TimeSpan.TicksPerSecond / 2);
        var f = new Fixture(windowEnd);
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        var onTheBoundary = f.StoreUnrelated(StrayRowId, "last-second-event", WindowEnd);

        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(onTheBoundary.Id);
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RowOnAnotherCalendarInsideTheSameWindow_SurvivesThePrune()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);

        // Bob's calendar was never fetched, so nothing about this reconcile is a statement about it.
        var bobsRow = f.StoreUnrelated(StrayRowId, "bobs-event", OldSlot.AddHours(2));
        bobsRow.OwnerCalendarInfoId = BobCalId;

        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(bobsRow.Id);
        f.StoredGoogleEventIds.Should().Contain("bobs-event");
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RowAConcurrentSyncStoresAfterTheFetch_SurvivesThePrune()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        // The sync worker runs alongside this write by design. Model one of its inserts landing
        // after the window fetch was taken: an event created on a phone, which Google holds and this
        // fetch could not have mentioned. Nothing but the ORDER of the prune's candidate read
        // protects it, and no incremental sync would bring it back.
        f.OnWindowFetched(() => f.StoreUnrelated(StrayRowId, "created-on-a-phone", OldSlot.AddHours(4)));

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(StrayRowId);
        f.StoredGoogleEventIds.Should().Contain("created-on-a-phone");
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_SeriesMasterRow_SurvivesThePrune()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);

        // A row whose GoogleEventId is a bare series id is missing from the fetch because
        // singleEvents=true expands series into instances and never returns the master resource —
        // not because Google dropped it. The recurrence-on path has such a row stored while this
        // reconcile runs, and removing it is that path's decision to make, not this one's.
        var masterRow = f.StoreUnrelated(MasterRowId, SeriesId, OldSlot.AddHours(3));

        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(masterRow.Id);
        f.StoredGoogleEventIds.Should().Contain(SeriesId);
        f.DeletedRowIds.Should().Contain(EditedRowId);
    }

    // ── When the fetch is no statement of the window ──────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_WhenTheWindowFetchComesBackEmpty_RemovesNothing()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        f.StoreInstance(SecondRowId, InstanceId(OldSlot.AddDays(7)), OldSlot.AddDays(7));

        // An empty answer is indistinguishable from a page whose body did not deserialise, and this
        // reconcile has just written into the window it is asking about.
        f.ArrangeWindowFetch([]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().BeEmpty();
        f.StoredGoogleEventIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_WhenTheWindowFetchFillsThePageBudget_RemovesNothing()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);

        // At the page budget the fetch may have been truncated — GetEventsAsync stops after
        // MaxSyncPages pages and returns what it has — so absence from it proves nothing.
        var atBudget = Enumerable.Range(0, GoogleCalendarClient.MaxWindowFetchEvents)
            .Select(i => f.FetchedInstance($"bulk-{i}", NewSlot))
            .ToList();
        f.ArrangeWindowFetch(atBudget);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.DeletedRowIds.Should().NotContain(EditedRowId);
    }

    // ── The tombstone path, unchanged ─────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_TombstonedInstance_IsStillRemovedExactlyOnce()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        var cancelled = f.StoreInstance(SecondRowId, InstanceId(OldSlot.AddDays(7)), OldSlot.AddDays(7));

        // Google names a cancelled instance explicitly; the tombstone branch removes it by id. The
        // prune must not then remove it a second time, which is also why the fetched ids it compares
        // against include the ones the tombstones name.
        f.ArrangeWindowFetch([
            f.FetchedInstance(InstanceId(NewSlot), NewSlot),
            new CalendarEvent { GoogleEventId = cancelled.GoogleEventId, Title = "CANCELLED_TOMBSTONE" }
        ]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        f.Repo.Verify(r => r.DeleteEventAsync(SecondRowId, It.IsAny<CancellationToken>()), Times.Once);
        f.StoredGoogleEventIds.Should().BeEquivalentTo([InstanceId(NewSlot)]);
    }

    // ── The window and calendar the prune is bounded by ───────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_ReadsPruneCandidatesForTheOwnerCalendarAndTheFetchedWindow()
    {
        var f = new Fixture();
        var edited = f.StoreInstance(EditedRowId, InstanceId(OldSlot), OldSlot);
        f.ArrangeWindowFetch([f.FetchedInstance(InstanceId(NewSlot), NewSlot)]);

        await f.Sut.UpdateRecurringAsync(edited.Id, MoveTo(NewSlot), RecurrenceScope.AllInSeries);

        // The bounds are the stored sync window the fetch itself used, and the calendar is the
        // event's owner — never "every calendar" and never a window of the reconcile's own making.
        f.Repo.Verify(r => r.GetEventsByOwnerCalendarAsync(
            AliceCalId, WindowStart, WindowEnd, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.GetEventsByOwnerCalendarAsync(
            It.Is<Guid>(id => id != AliceCalId), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static UpdateEventRequest MoveTo(DateTimeOffset start) =>
        new("Weekly", start, start.AddHours(1), false, "Loc", "Body");

    /// <summary>
    /// Mocked dependencies plus a stand-in for the stored rows, so a prune that deletes the wrong
    /// row shows up as a missing row rather than as an un-asserted mock call.
    /// </summary>
    private sealed class Fixture
    {
        public readonly Mock<IGoogleCalendarClient> Google = new();
        public readonly Mock<ICalendarRepository> Repo = new();
        public readonly RecordingLogger<CalendarEventService> Logger = new();
        public readonly CalendarEventService Sut;

        public readonly List<Guid> DeletedRowIds = [];

        private readonly List<CalendarEvent> _stored = [];
        private readonly DateTimeOffset _windowEnd;
        private Action? _afterWindowFetch;

        private readonly CalendarInfo _alice = new() { Id = AliceCalId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice" };
        private readonly CalendarInfo _bob = new() { Id = BobCalId, GoogleCalendarId = "bob@google.com", DisplayName = "Bob" };
        private readonly CalendarInfo _shared = new() { Id = SharedCalId, GoogleCalendarId = "shared@google.com", DisplayName = "Family", IsShared = true };

        public Fixture(DateTimeOffset? windowEnd = null)
        {
            _windowEnd = windowEnd ?? WindowEnd;

            var currentUser = new Mock<ICurrentUserService>();
            currentUser.SetupGet(u => u.UserId).Returns("u-1");

            // Real parser: the members-tag normalise/parse logic is pure computation with no
            // behaviour worth substituting, and the prune reads the rows it produces.
            var realParser = new MemberTagParser();
            var tagParser = new Mock<IMemberTagParser>();
            tagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names) => realParser.NormaliseDescription(d, names));
            tagParser.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names, IReadOnlyList<string>? tagged) => realParser.ParseMembers(d, names, tagged));
            tagParser.Setup(p => p.ExtractTaggedMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names) => realParser.ExtractTaggedMembers(d, names));

            Repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([_alice, _bob, _shared]);
            Repo.Setup(r => r.GetCalendarByIdAsync(AliceCalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(_alice);
            Repo.Setup(r => r.GetSyncStateAsync(AliceCalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new SyncState
                {
                    CalendarInfoId = AliceCalId,
                    SyncWindowStart = WindowStart,
                    SyncWindowEnd = _windowEnd
                });
            Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

            // The stored rows, read and written the way the repository does.
            Repo.Setup(r => r.GetEventAsync(It.IsAny<Guid>(), "u-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, string _, CancellationToken _) => _stored.FirstOrDefault(e => e.Id == id));
            Repo.Setup(r => r.GetEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => _stored.FirstOrDefault(e => e.Id == id));
            Repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string googleEventId, CancellationToken _) =>
                    _stored.FirstOrDefault(e => e.GoogleEventId == googleEventId));
            Repo.Setup(r => r.GetEventsBySeriesIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string seriesId, CancellationToken _) =>
                    _stored.Where(e => e.GoogleRecurringEventId == seriesId).ToList());

            // The candidate read, with the repository's documented predicate: scoped to one calendar
            // and matching on OVERLAP. Deliberately the WIDE predicate — narrowing it here would
            // move the behaviour under test into the test double.
            Repo.Setup(r => r.GetEventsByOwnerCalendarAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid calendarId, DateTimeOffset start, DateTimeOffset end, CancellationToken _) =>
                    _stored.Where(e => e.OwnerCalendarInfoId == calendarId && e.Start < end && e.End > start).ToList());

            Repo.Setup(r => r.AddEventAsync(It.IsAny<CalendarEvent>(), It.IsAny<CancellationToken>()))
                .Callback((CalendarEvent e, CancellationToken _) => _stored.Add(e));
            Repo.Setup(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, CancellationToken _) =>
                {
                    DeletedRowIds.Add(id);
                    _stored.RemoveAll(e => e.Id == id);
                });

            Google.Setup(g => g.PatchEventFieldsAsync(
                    It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _, EventReminders? _) => e);

            // A resolvable master, which is production's overwhelming majority: an unresolvable one
            // refuses the timing change outright and never reaches the reconcile.
            Google.Setup(g => g.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeriesMaster(Rrule, OldSlot));

            Sut = new CalendarEventService(
                Google.Object, Repo.Object, new Mock<ICalendarMigrationService>().Object, tagParser.Object,
                new Mock<IOutboundWriteHashCache>().Object, currentUser.Object,
                new NodaTimeRecurrenceTimeZoneFactory(), Logger);
        }

        public IReadOnlyList<string> StoredGoogleEventIds => _stored.Select(e => e.GoogleEventId).ToList();

        /// <summary>A stored instance of the series this edit touches.</summary>
        public CalendarEvent StoreInstance(Guid id, string googleEventId, DateTimeOffset start)
        {
            var row = new CalendarEvent
            {
                Id = id,
                GoogleEventId = googleEventId,
                Title = "Weekly",
                Start = start,
                End = start.AddHours(1),
                Description = "Body\n[members: Alice]",
                OwnerCalendarInfoId = AliceCalId,
                GoogleRecurringEventId = SeriesId,
                RecurrenceRule = Rrule,
                Members = [_alice]
            };
            _stored.Add(row);
            return row;
        }

        /// <summary>A stored row with no connection to the series being edited.</summary>
        public CalendarEvent StoreUnrelated(Guid id, string googleEventId, DateTimeOffset start)
        {
            var row = new CalendarEvent
            {
                Id = id,
                GoogleEventId = googleEventId,
                Title = "Something else",
                Start = start,
                End = start.AddHours(1),
                Description = "Body\n[members: Alice]",
                OwnerCalendarInfoId = AliceCalId,
                Members = [_alice]
            };
            _stored.Add(row);
            return row;
        }

        /// <summary>An instance as the window fetch returns it: pass 1, so no RRULE.</summary>
        public CalendarEvent FetchedInstance(string googleEventId, DateTimeOffset start) => new()
        {
            GoogleEventId = googleEventId,
            Title = "Weekly",
            Start = start,
            End = start.AddHours(1),
            Description = "Body\n[members: Alice]",
            GoogleRecurringEventId = SeriesId
        };

        public void ArrangeWindowFetch(IReadOnlyList<CalendarEvent> instances) =>
            Google.Setup(g => g.GetEventsAsync(GoogleCalId, WindowStart, _windowEnd, null, It.IsAny<CancellationToken>()))
                .Callback(() => _afterWindowFetch?.Invoke())
                .ReturnsAsync((instances, (string?)null));

        /// <summary>Runs when the window fetch is taken, to model a concurrent writer's timing.</summary>
        public void OnWindowFetched(Action action) => _afterWindowFetch = action;
    }
}
