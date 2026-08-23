using System.Net;
using System.Runtime.CompilerServices;
using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

/// <summary>
/// FHQ-18.4 — service-layer recurring edit/delete scopes. Verifies the Google call shapes for
/// each of the three scopes, the local reconcile (re-fetch window + upsert/remove by GoogleEventId),
/// the members-tag normalisation and member-scope rejection (spec §10.1), the N-per-write echo-guard
/// hash recording (spec §10.2), the series-level 1↔N migration, and the non-recurring fail-fast guard.
/// </summary>
public class CalendarEventServiceRecurringTests
{
    private static readonly Guid AliceCalId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid BobCalId     = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SharedCalId  = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid EventId       = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private const string SeriesId          = "series-master-id";
    private const string GoogleCalId       = "alice@google.com";
    private const string SharedGoogleCalId = "shared@google.com";

    private static readonly DateTimeOffset WindowStart = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd   = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InstanceStart = new(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

    // ── ThisOnly edit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_ThisOnly_PatchesInstanceAndUpsertsSingleRow()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);
        f.ArrangeExistingRow(instance); // reconcile finds the instance row and upserts it
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-2", InstanceStart, isException: true)]);

        var request = Req("Updated Title", InstanceStart, "Lunch");
        await f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.ThisOnly);

        // Patches this instance's own GoogleEventId (events.patch), not the master.
        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-2"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // One row upserted from the reconcile; OriginalStartTime populated from the exception response.
        f.Repo.Verify(r => r.UpdateEventAsync(It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-2" && e.OriginalStartTime != null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisOnly_NormalisesDescription()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-2", InstanceStart, isException: true)]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("T", InstanceStart, "Lunch"), RecurrenceScope.ThisOnly);

        f.TagParser.Verify(p => p.NormaliseDescription("Lunch", It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    // ── ThisAndFollowing edit ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_MakesTwoGoogleCallsAndSplitsRows()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU";
        f.ArrangeEvent(instance);

        // Local rows belonging to the original series, some at/after the split point.
        var before = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart.AddDays(-7));
        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        var after = f.RecurringInstance(Guid.NewGuid(), "inst-3", InstanceStart.AddDays(7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([before, atSplit, after]);

        // New series id assigned by the insert call.
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-series-id"; return e; });

        f.ArrangeReconcileWindow([
            f.GoogleInstance("new-inst-1", InstanceStart, recurringId: "new-series-id"),
            f.GoogleInstance("new-inst-2", InstanceStart.AddDays(7), recurringId: "new-series-id")
        ]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        // Exactly TWO Google writes: insert the new series, then truncate the original master.
        // (FHQ-173 put the create first; the ORDER itself is pinned by the split-ordering tests below.)
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(GoogleCalId, SeriesId, It.Is<string>(s => s.Contains("UNTIL=")), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.Is<string>(r => r.Contains("FREQ=WEEKLY")), It.IsAny<CancellationToken>()), Times.Once);

        // The truncated original's rows with Start >= split point are removed (inst-2, inst-3); inst-1 kept.
        f.Repo.Verify(r => r.DeleteEventAsync(atSplit.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(after.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(before.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_UntilSeries_NewSeriesKeepsSameUntil()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;UNTIL=20260601T000000Z";
        f.ArrangeEvent(instance);

        string? capturedNewRule = null;
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent e, string _, string r, CancellationToken _) => { capturedNewRule = r; e.GoogleEventId = "new-series-id"; })
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => e);
        f.ArrangeReconcileWindow([f.GoogleInstance("new-inst-1", InstanceStart, recurringId: "new-series-id")]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        // The forward series preserves the original UNTIL rather than running forever.
        capturedNewRule.Should().Contain("UNTIL=20260601T000000Z");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeries_ForwardSeriesCarriesRemainingCount()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10";
        f.ArrangeEvent(instance);

        // Original series rows: one Sunday before the split (inst-1), the split itself (inst-2),
        // and one after (inst-3). Occurrences strictly before the split = 1 (inst-1), so the
        // forward series must carry COUNT = 10 - 1 = 9.
        var before = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart.AddDays(-7));
        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        var after = f.RecurringInstance(Guid.NewGuid(), "inst-3", InstanceStart.AddDays(7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([before, atSplit, after]);

        // The master IS resolvable, and its DTSTART is the first synced occurrence — the count is
        // anchored there. (FHQ-172: an unresolvable master no longer counts from a local proxy, it
        // refuses, so this test states the anchor it means to exercise instead of relying on null.)
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10", before.Start));

        string? capturedNewRule = null;
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent e, string _, string r, CancellationToken _) => { capturedNewRule = r; e.GoogleEventId = "new-series-id"; })
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => e);
        f.ArrangeReconcileWindow([f.GoogleInstance("new-inst-1", InstanceStart, recurringId: "new-series-id")]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        capturedNewRule.Should().Contain("COUNT=9");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeries_AnchorsRemainingAtTrueMasterStart()
    {
        // The master series began FIVE Sundays BEFORE the earliest locally-synced row: the sync window
        // does not reach back to the master's DTSTART. Anchoring the remaining-COUNT enumeration at the
        // earliest LOCAL row would under-count the occurrences before the split and leave the forward
        // series too long. The true master start (fetched via GetSeriesMasterAsync) must anchor it.
        var f = new Fixture();
        var masterStart = new DateTimeOffset(2026, 2, 8, 9, 0, 0, TimeSpan.Zero); // Sunday, 5 weeks before InstanceStart (Mar 15... actually Mar 8)
        var splitStart = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);  // Sunday

        var instance = f.RecurringInstance(EventId, "inst-split", splitStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10";
        f.ArrangeEvent(instance);

        // Only two locally-synced rows, both within the window and AFTER the master start.
        var localBefore = f.RecurringInstance(Guid.NewGuid(), "inst-prev", splitStart.AddDays(-7)); // Mar 8
        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-split", splitStart);                // Mar 15
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([localBefore, atSplit]);

        // Master fetch yields the true DTSTART (Feb 8): occurrences strictly before Mar 15 are
        // Feb 8, 15, 22, Mar 1, Mar 8 = 5, so the forward series must carry COUNT = 10 - 5 = 5.
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10", masterStart));

        string? capturedNewRule = null;
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent e, string _, string r, CancellationToken _) => { capturedNewRule = r; e.GoogleEventId = "new-series-id"; })
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => e);
        f.ArrangeReconcileWindow([f.GoogleInstance("new-inst-1", splitStart, recurringId: "new-series-id")]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        f.Google.Verify(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()), Times.Once);
        capturedNewRule.Should().Contain("COUNT=5");
    }

    // ── FHQ-161: the split count must enumerate in the SERIES' zone ────────────────────────────
    //
    // Both inputs to the count are true wall-clock-anchored instants — the master DTSTART Google
    // returns, and the split instance's Start as synced into the DB. Enumerating between them at
    // fixed UTC instants drifts against the real occurrence boundaries once the series crosses a DST
    // transition, and the forward series silently loses occurrences.
    //
    // Worked example (the ticket's): weekly Tuesday 19:00 Europe/London, COUNT=5, from Tue
    // 13 Oct 2026. The UK clocks go back on Sun 25 Oct, so Google's occurrences are
    //   13 Oct 18:00Z · 20 Oct 18:00Z · 27 Oct 19:00Z · 3 Nov 19:00Z · 10 Nov 19:00Z.

    private const string LondonZoneId = "Europe/London";
    private const string WeeklyTuesdayCount5 = "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=5";
    private static readonly DateTimeOffset AutumnSeriesStart = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeriesAcrossAutumnTransition_KeepsEveryOccurrence()
    {
        // Split at occurrence 4 (Tue 3 Nov, 19:00 GMT). Three occurrences precede it, so the forward
        // series must carry COUNT = 5 − 3 = 2. Counting at fixed UTC instants places occurrence 4's
        // twin at 18:00Z — before the 19:00Z split — giving before = 4 and COUNT = 1, which DELETES
        // occurrence 5 from the family's calendar.
        var f = new Fixture();
        var splitStart = new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

        var capturedNewRule = ArrangeCountSplit(f, WeeklyTuesdayCount5, AutumnSeriesStart, LondonZoneId, splitStart);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        capturedNewRule.Value.Should().Contain("COUNT=2");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_SplitAtFinalOccurrenceAcrossAutumnTransition_IsNotFalselyRejected()
    {
        // COUNT=4, split at the LAST occurrence (Tue 3 Nov, 19:00 GMT): before = 3, remaining = 1 —
        // a legitimate split. The fixed-UTC count returns before = 4 → remaining = 0 →
        // InvalidSeriesSplitException, rejecting a valid edit with a false explanation.
        var f = new Fixture();
        var splitStart = new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

        var capturedNewRule = ArrangeCountSplit(
            f, "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=4", AutumnSeriesStart, LondonZoneId, splitStart);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Football training", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().NotThrowAsync<InvalidSeriesSplitException>();
        capturedNewRule.Value.Should().Contain("COUNT=1");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeriesAcrossSpringTransition_KeepsEveryOccurrence()
    {
        // The spring transition is benign for this path: the enumerated twin lands an hour LATE and
        // is correctly excluded either way. Pinned so a future change cannot silently break the
        // direction that already worked. Weekly Tue 19:00 from 17 Mar 2026; clocks forward 29 Mar.
        var f = new Fixture();
        var seriesStart = new DateTimeOffset(2026, 3, 17, 19, 0, 0, TimeSpan.Zero); // 19:00 GMT
        var splitStart = new DateTimeOffset(2026, 4, 7, 18, 0, 0, TimeSpan.Zero);   // occurrence 4, 19:00 BST

        var capturedNewRule = ArrangeCountSplit(f, WeeklyTuesdayCount5, seriesStart, LondonZoneId, splitStart);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        capturedNewRule.Value.Should().Contain("COUNT=2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Not/AZone")]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TimedCountSeriesWithNoUsableZone_WarnsAndUsesTheFixedUtcFallback(string? timeZoneId)
    {
        // DELIBERATE fallback, pinned here so it cannot change silently. When Google supplies no
        // usable zone the count degrades to the legacy fixed-UTC enumeration rather than rejecting
        // the edit — no worse than the pre-FHQ-161 behaviour. On a TIMED series that is not
        // DST-aware and is the one case genuinely worth a Warning.
        var f = new Fixture();
        var splitStart = new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

        var capturedNewRule = ArrangeCountSplit(f, WeeklyTuesdayCount5, AutumnSeriesStart, timeZoneId, splitStart);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        capturedNewRule.Value.Should().Contain("COUNT=1", "the zone-less count over-counts by one across a fall-back transition");
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning
            && r.Message.Contains("no usable IANA time zone")
            && r.Message.Contains(SeriesId));
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_AllDayCountSeriesWithNoZone_DoesNotWarn()
    {
        // An all-day master carries no start.timeZone BY DESIGN and is date-anchored, so fixed-UTC
        // enumeration is exact for it — expected and handled, which the logging standard keeps out
        // of Warning. Warning here would bury the timed case above, the only one that matters.
        var f = new Fixture();
        var splitStart = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);

        ArrangeCountSplit(f, WeeklyTuesdayCount5, AutumnSeriesStart, masterTimeZone: null, splitStart, isAllDay: true);

        await f.Sut.UpdateRecurringAsync(
            EventId, Req("Bin day", splitStart, "Body", isAllDay: true), RecurrenceScope.ThisAndFollowing);

        f.Logger.Records.Should().NotContain(r => r.Level == LogLevel.Warning && r.Message.Contains("IANA time zone"));
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Debug
            && r.Message.Contains("fixed-UTC recurrence enumeration")
            && r.Message.Contains(SeriesId));
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeriesWithAResolvableZone_DoesNotWarn()
    {
        var f = new Fixture();
        var splitStart = new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

        ArrangeCountSplit(f, WeeklyTuesdayCount5, AutumnSeriesStart, LondonZoneId, splitStart);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", splitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        f.Logger.Records.Should().NotContain(r => r.Message.Contains("no usable IANA time zone"));
    }

    // FHQ-172 moved the unresolvable-master COUNT split from "degrade to a local proxy" to "refuse
    // and write nothing"; that behaviour is covered by the FHQ-172 section further down this file
    // (search "FHQ-172: an unresolvable series master must never yield a written anchor"), which
    // also carries the one-Warning-per-incident assertions this test used to own.

    // Arranges a "this and following" split of a COUNT-bounded series whose master carries the given
    // RRULE, DTSTART and IANA zone, and captures the RRULE the forward series is created with.
    private static StrongBox<string?> ArrangeCountSplit(
        Fixture f, string rrule, DateTimeOffset masterStart, string? masterTimeZone, DateTimeOffset splitStart,
        bool isAllDay = false)
    {
        var instance = f.RecurringInstance(EventId, "inst-split", splitStart);
        instance.RecurrenceRule = rrule;
        instance.IsAllDay = isAllDay;
        f.ArrangeEvent(instance);

        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([instance]);

        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster(rrule, masterStart, masterTimeZone));

        var captured = new StrongBox<string?>(null);
        f.Google.Setup(g => g.CreateRecurringEventAsync(
                GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent e, string _, string r, CancellationToken _) =>
            {
                captured.Value = r;
                e.GoogleEventId = "new-series-id";
            })
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => e);

        f.ArrangeReconcileWindow([f.GoogleInstance("new-inst-1", splitStart, recurringId: "new-series-id")]);
        return captured;
    }

    // ── FHQ-164 / FHQ-170: the series-zone discovery ladder ───────────────────────────────────
    //
    // FHQ-161 closed the happy path — the master supplies the zone. This closes the rest of it:
    // rather than substituting the family's configured zone (a proxy, since most events are created
    // on a phone), the zone is ASKED OF GOOGLE, strictly ordered by provenance:
    //   1. stored on the series' own rows   3. any surviving instance (events.get)
    //   2. the series master                4. the calendar's default zone
    //   5. terminal: fixed-UTC, announced at Warning for a timed series.
    //
    // The same worked example throughout: weekly Tuesday 19:00 Europe/London, COUNT=5, from Tue
    // 13 Oct 2026, split at occurrence 4 (Tue 3 Nov, 19:00 GMT). Zone-aware → COUNT=2. Fixed-UTC
    // over-counts by one across the 25 Oct fall-back → COUNT=1, deleting occurrence 5.

    private const string NewYorkZoneId = "America/New_York";
    private static readonly DateTimeOffset AutumnSplitStart = new(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_MasterWithNoZoneButZoneStored_KeepsEveryOccurrence()
    {
        // FHQ-164's headline case, restated for FHQ-172. It used to be posed as "the master 404s",
        // but that case no longer reaches the count at all — it is refused. The zone gap it closes is
        // real either way: a master that resolves and carries NO start.timeZone (Google omits it on
        // an all-day master, and older masters can lack it) would otherwise fall to the fixed-UTC
        // enumeration and silently drop the series' last occurrence. The stored zone closes it.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: LondonZoneId, masterZone: null);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=2");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_MasterWithNoZoneAndNoStoredZone_SurvivingInstanceSuppliesIt()
    {
        // Rung 3. A recurring instance carries the series' start.timeZone and every instance's id is
        // already in seriesRows, so one events.get recovers the zone the master could not give.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=2");
        f.Google.Verify(g => g.GetEventAsync(GoogleCalId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_SplitAtFinalOccurrence_ResolvableZone_IsNotFalselyRejected()
    {
        // COUNT=4 split at the LAST occurrence: before = 3, remaining = 1 — legitimate. The fixed-UTC
        // count returns before = 4 → remaining = 0 → InvalidSeriesSplitException with a false
        // explanation. Any rung of the ladder that yields makes that impossible.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: null, instanceZone: LondonZoneId,
            rrule: "RRULE:FREQ=WEEKLY;BYDAY=TU;COUNT=4");

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().NotThrowAsync<InvalidSeriesSplitException>();
        split.Rule.Value.Should().Contain("COUNT=1");
    }

    // ── Ladder order: a higher rung's value wins over a lower one's ────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_StoredZoneWinsOverTheMasters_AndCostsNoCall()
    {
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: LondonZoneId, masterZone: NewYorkZoneId,
            instanceZone: NewYorkZoneId, calendarDefaultZone: NewYorkZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
        f.Google.Verify(g => g.GetEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "rung 1 is a local read — reaching Google for a value already stored is pure latency");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_MasterZoneWinsOverASurvivingInstances()
    {
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: LondonZoneId,
            instanceZone: NewYorkZoneId, calendarDefaultZone: NewYorkZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
        f.Google.Verify(g => g.GetEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "the master fetch already happened for the anchor, so rung 3 is only reached when rung 2 gave nothing");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_InstanceZoneWinsOverTheCalendarDefault()
    {
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: null,
            instanceZone: LondonZoneId, calendarDefaultZone: NewYorkZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CalendarDefaultIsUsedWhenNothingAboveItYields()
    {
        // Rung 4 is what Google itself would apply to an event on this calendar with no zone of its
        // own — still a Google-supplied value, so no Warning and no guess.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=2");
        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
        f.Logger.Records.Should().NotContain(r => r.Level == LogLevel.Warning && r.Message.Contains("IANA time zone"));
    }

    // ── The terminal rung ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_EveryRungExhausted_WarnsNamingTheSeriesAndStillCompletes()
    {
        // Decision 3a: never fail a user's edit over a zone lookup. The one genuinely-guessing case
        // left announces itself instead of passing silently.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: null);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=1", "fixed-UTC over-counts by one across the fall-back transition");
        split.Created.Value.Should().NotBeNull("the edit completes rather than failing on a zone lookup");
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Warning
            && r.Message.Contains("no usable IANA time zone")
            && r.Message.Contains(SeriesId));
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_EveryRungExhaustedOnAnAllDaySeries_StaysAtDebug()
    {
        // An all-day series legitimately carries no zone and is date-anchored, so fixed-UTC
        // enumeration is EXACT for it. Warning here would bury the timed case, the only one that
        // matters (logging standard: expected-and-handled conditions are not Warnings).
        var f = new Fixture();
        ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: null, isAllDay: true);

        await f.Sut.UpdateRecurringAsync(
            EventId, Req("Bin day", AutumnSplitStart, "Body", isAllDay: true), RecurrenceScope.ThisAndFollowing);

        f.Logger.Records.Should().NotContain(r => r.Level == LogLevel.Warning && r.Message.Contains("IANA time zone"));
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Debug && r.Message.Contains("fixed-UTC recurrence enumeration") && r.Message.Contains(SeriesId));
    }

    // ── Lazy backfill (Decision 4) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ZoneResolvedFromTheMaster_IsPersistedOntoTheSeriesRows()
    {
        // Normal sync fetches a master only when the RRULE is not already cached, so an existing
        // series would never re-fetch one. Persisting what a fetch DID report is what makes the
        // backfill happen at all — and makes the next edit resolve at rung 1 with no call.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rows.Should().OnlyContain(r => r.IanaTimeZone == LondonZoneId);
        f.Repo.Verify(r => r.UpdateEventAsync(It.Is<CalendarEvent>(e => e.IanaTimeZone == LondonZoneId), It.IsAny<CancellationToken>()),
            Times.AtLeast(split.Rows.Count));
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ZoneResolvedFromAnInstance_IsPersistedOntoTheSeriesRows()
    {
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rows.Should().OnlyContain(r => r.IanaTimeZone == LondonZoneId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TheCalendarsDefaultZoneIsNotPersistedOntoTheSeriesRows()
    {
        // Rung 4 is the CALENDAR's value, not the series'. Writing it onto the series would fabricate
        // a provenance the rows do not have and would stop a later, better rung from ever running.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rows.Should().OnlyContain(r => r.IanaTimeZone == null);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_NoZoneObserved_LeavesTheStoredRowsNull()
    {
        // Existing production rows stay null until Google actually reports a zone — no schema
        // default, nothing that pretends to know.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: null);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rows.Should().OnlyContain(r => r.IanaTimeZone == null);
    }

    // ── The reconcile is a Google fetch like any other, so it backfills too ───────────────────

    [Theory]
    [InlineData(LondonZoneId, null, LondonZoneId)]        // Google reports one → stored
    [InlineData(LondonZoneId, NewYorkZoneId, LondonZoneId)]  // Google is the system of record
    [InlineData(null, LondonZoneId, LondonZoneId)]        // all-day / absent: the stored value survives
    [InlineData("", LondonZoneId, LondonZoneId)]          // blank is absent, not a new value
    public async Task UpdateRecurringAsync_ThisOnly_ReconcileBackfillsTheAnchorZoneWithoutEverBlankingIt(
        string? fetchedZone, string? storedZone, string? expectedZone)
    {
        // Blanking here would be invisible until the NEXT edit, which would then find no zone and
        // hand the write to the family-zone fallback — FHQ-170, one step removed from its cause.
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        var storedRow = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        storedRow.IanaTimeZone = storedZone;
        f.ArrangeExistingRow(storedRow);

        var fetched = f.GoogleInstance("inst-2", InstanceStart, isException: true);
        fetched.IanaTimeZone = fetchedZone;
        f.ArrangeReconcileWindow([fetched]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated Title", InstanceStart, "Lunch"), RecurrenceScope.ThisOnly);

        storedRow.IanaTimeZone.Should().Be(expectedZone);
    }

    // ── Every rung is filtered for usability ──────────────────────────────────────────────────
    //
    // Google's zone names run ahead of a bundled tz database (Europe/Kyiv, America/Ciudad_Juarez are
    // the real-world instances; which of them the shipped tzdb knows depends on its version, so the
    // tests below use an unmistakably fictional id and stay deterministic). A rung that hands back an
    // id the factory cannot resolve must not short-circuit the ladder: the count would fall to
    // fixed-UTC while a rung below still had a zone that works.

    private const string UnknownZoneId = "Mars/Olympus_Mons";

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_StoredZoneTheTzDatabaseRejects_FallsThroughToTheNextRung()
    {
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: UnknownZoneId, masterZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=2", "rung 2's zone counts the fall-back transition correctly");
        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Debug && r.Message.Contains("does not recognise") && r.Message.Contains(SeriesId));
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ZoneTheTzDatabaseRejects_IsNotPersistedOntoTheSeriesRows()
    {
        // Storing an id neither consumer can use would make the next edit resolve to the same dead
        // end at rung 1, for free, forever.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: null, masterZone: UnknownZoneId, instanceZone: null, calendarDefaultZone: null);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rows.Should().OnlyContain(r => r.IanaTimeZone == null);
        f.Repo.Verify(r => r.UpdateEventAsync(It.Is<CalendarEvent>(e => e.IanaTimeZone == UnknownZoneId), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Rung 3's probing: bounded, and never swallowing a signal that is not about zones ──────

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_InstanceProbesAreBoundedNotOnePerOccurrence()
    {
        // A master that 404s because the series was genuinely deleted takes its instances with it, so
        // every probe 404s too. Unbounded, that costs one call per synced occurrence on the way to
        // the same answer. Four rows are arranged; only the bound stops the fourth probe.
        var f = new Fixture();
        ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: LondonZoneId);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        f.Google.Verify(g => g.GetEventAsync(GoogleCalId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3),
            "the ladder probes at most MaxInstanceZoneProbes instances before dropping to the next rung");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_InstanceProbeNeedingReauth_PropagatesRatherThanDegradingTheZone()
    {
        // Swallowing a reauth here would degrade the zone silently AND let the edit carry on to a
        // write that then fails — masking the very signal FHQ-82/85/153 exist to surface. Neither
        // reauth nor cancellation is a zone problem, so neither is the probe loop's to handle.
        var f = new Fixture();
        ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: LondonZoneId);
        f.Google.Setup(g => g.GetEventAsync(GoogleCalId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "invalid_grant"));

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().ThrowAsync<GoogleReauthRequiredException>();
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_InstanceProbeFailingTransiently_TriesTheNextCandidate()
    {
        // A transient upstream failure IS the probe loop's to handle: the ladder has rungs below it
        // and a user's edit is never failed over a zone lookup (Decision 3a). Reported at Debug —
        // expected-and-handled conditions are not Warnings.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: null, masterZone: null, instanceZone: null, calendarDefaultZone: null);

        var probes = 0;
        f.Google.Setup(g => g.GetEventAsync(GoogleCalId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string id, CancellationToken _) => ++probes == 1
                ? throw new GoogleApiException(HttpStatusCode.ServiceUnavailable, "GetEvent")
                : new GoogleEventDetail(id, null, LondonZoneId));

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("COUNT=2", "the second candidate supplied the zone the first could not");
        f.Logger.Records.Should().Contain(r =>
            r.Level == LogLevel.Debug && r.Message.Contains("could not be fetched") && r.Message.Contains(SeriesId));
        f.Logger.Records.Should().NotContain(r => r.Level == LogLevel.Warning && r.Message.Contains("IANA time zone"));
    }

    // ── FHQ-170 on the series-level write paths ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_PatchesTheMasterWithTheSeriesOwnZone()
    {
        // The AllInSeries patch builds a master object from scratch. With no zone on it the client
        // falls through to the family's configured zone and re-anchors the whole series — FHQ-170 at
        // its most severe, because it moves every future occurrence at once.
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-3", InstanceStart);
        f.ArrangeEvent(instance);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", InstanceStart, NewYorkZoneId));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", InstanceStart)]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Weekly", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.IanaTimeZone == NewYorkZoneId),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_MasterWithoutAZone_FallsBackToTheStoredSeriesZone()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-3", InstanceStart);
        instance.IanaTimeZone = LondonZoneId;
        f.ArrangeEvent(instance);
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>())).ReturnsAsync([instance]);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", InstanceStart));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", InstanceStart)]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Weekly", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.IanaTimeZone == LondonZoneId),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(NewYorkZoneId, LondonZoneId, NewYorkZoneId)]  // both present and differing → the master's wins
    [InlineData(null, LondonZoneId, LondonZoneId)]            // no master zone → the stored series zone
    [InlineData("", LondonZoneId, LondonZoneId)]              // blank is absent, not a value that beats a good one
    public async Task UpdateRecurringAsync_AllInSeries_MasterZoneLeadsTheStoredOne(
        string? masterZone, string? storedZone, string? expectedZone)
    {
        // The two arms have to be exercised TOGETHER or the precedence is not pinned at all: with one
        // arm null either ordering passes. The master's own zone leads here — unlike the counting
        // ladder — because the edited row may be an EXCEPTION instance carrying a zone of its own
        // that is not the one the series is anchored to.
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-3", InstanceStart);
        instance.IanaTimeZone = storedZone;
        f.ArrangeEvent(instance);
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>())).ReturnsAsync([instance]);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", InstanceStart, masterZone));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", InstanceStart)]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Weekly", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.IanaTimeZone == expectedZone),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_UntilSeries_ForwardSeriesKeepsTheStoredZoneWithoutExtraCalls()
    {
        // A Never/UNTIL split has no count to enumerate, so the ladder's fetching rungs would be pure
        // latency — but the forward series is still a CONTINUATION and must keep its anchor zone.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: LondonZoneId, rrule: "RRULE:FREQ=WEEKLY;BYDAY=TU;UNTIL=20261201T000000Z");

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Created.Value!.IanaTimeZone.Should().Be(LondonZoneId);
        f.Google.Verify(g => g.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Google.Verify(g => g.GetEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Arranges a "this and following" split of the worked-example series across the October
    /// fall-back transition, wiring each rung of the discovery ladder independently so a test can
    /// state exactly which rungs are available. Captures both the RRULE the forward series is
    /// created with and the event object itself (which carries its anchor zone).
    /// </summary>
    private static LadderSplit ArrangeLadderSplit(
        Fixture f,
        string? storedZone = null,
        string? masterZone = null,
        bool masterResolves = true,
        string? instanceZone = null,
        string? calendarDefaultZone = null,
        string rrule = WeeklyTuesdayCount5,
        bool isAllDay = false)
    {
        // The four synced occurrences of weekly Tuesday 19:00 Europe/London from 13 Oct 2026; the
        // last of them is the split point. 27 Oct and 3 Nov sit after the 25 Oct fall-back, so their
        // UTC instants are an hour later than the first two — which is precisely what a fixed-UTC
        // enumeration cannot reproduce.
        var starts = new[]
        {
            AutumnSeriesStart,
            AutumnSeriesStart.AddDays(7),
            new DateTimeOffset(2026, 10, 27, 19, 0, 0, TimeSpan.Zero),
            AutumnSplitStart
        };

        var rows = new List<CalendarEvent>();
        for (var i = 0; i < starts.Length; i++)
        {
            var isSplitRow = i == starts.Length - 1;
            var row = f.RecurringInstance(isSplitRow ? EventId : Guid.NewGuid(), $"inst-{i}", starts[i]);
            row.RecurrenceRule = rrule;
            row.IsAllDay = isAllDay;
            row.IanaTimeZone = storedZone;
            rows.Add(row);
        }

        f.ArrangeEvent(rows[^1]);
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);

        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(masterResolves ? new SeriesMaster(rrule, AutumnSeriesStart, masterZone) : null);

        f.Google.Setup(g => g.GetEventAsync(GoogleCalId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string id, CancellationToken _) =>
                instanceZone is null ? null : new GoogleEventDetail(id, null, instanceZone));

        f.Alice.IanaTimeZone = calendarDefaultZone;

        var capturedRule = new StrongBox<string?>(null);
        var capturedEvent = new StrongBox<CalendarEvent?>(null);
        f.Google.Setup(g => g.CreateRecurringEventAsync(
                GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent e, string _, string r, CancellationToken _) =>
            {
                capturedRule.Value = r;
                capturedEvent.Value = e;
                e.GoogleEventId = "new-series-id";
            })
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => e);

        f.ArrangeReconcileWindow([f.GoogleInstance("new-inst-1", AutumnSplitStart, recurringId: "new-series-id")]);
        return new LadderSplit(capturedRule, capturedEvent, rows);
    }

    private sealed record LadderSplit(
        StrongBox<string?> Rule,
        StrongBox<CalendarEvent?> Created,
        IReadOnlyList<CalendarEvent> Rows);

    // ── FHQ-172: an unresolvable series master must never yield a written anchor ───────────────
    //
    // ResolveSeriesAnchorAsync degrades to the earliest LOCAL row when Google returns no master.
    // That row is a proxy: when the master predates the sync window it sits LATER than the true
    // origin. Both callers used to write it —
    //   * AllInSeries patched `proxy + shift` onto the master as its new DTSTART, relocating the
    //     series' origin forward and deleting every occurrence before the sync window from Google.
    //     `shift` is zero for a pure title edit, so RENAMING a series was enough to trigger it.
    //   * the COUNT split derived `remaining = COUNT − occurrences before the split` from it and
    //     wrote that count back, leaving the forward series too long.
    // Neither is permitted now: the split refuses, and the master patch omits start/end unless the
    // user actually asked for a timing change, in which case it refuses too.
    //
    // ONE WARNING PER INCIDENT (FHQ-161). These tests assert the COUNT of Warning records, not
    // merely that one is present: a `Contain(...)` cannot tell one Warning from three, and that gap
    // is exactly how a duplicate Warning regressed once already. The anchor site itself logs at
    // Debug — it decides nothing, and the omit-times rename below handles the missing origin
    // completely successfully, which the logging standard says is not a Warning at all. So the
    // budget is: zero from a successful degraded write, exactly one from each refusal.
    // (DomainExceptionHandler logs its own Warning when it maps the exception; that is outside this
    // logger and carries only status/method/path, so it is extra information, not a duplicate.)
    //
    // REACHABILITY, HONESTLY. After Change 1 (a master's start survives an absent RRULE) no
    // production shape is known in which the omit-times write both fires and succeeds — a master
    // that 404s on events.get would 404 on events.patch too. These tests drive the branch through
    // the mocked client because it is a deliberate guard against irreversible loss of series
    // history, not because it is the mechanism that fixed the reported defect.

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_MasterUnresolvableAndNothingRetimed_PatchesWithoutStartOrEnd()
    {
        // The renaming case. The edit must still land — the family asked for it and it is perfectly
        // expressible — but through the patch that sends no start/end, so events.patch's merge
        // leaves Google's own DTSTART exactly where it is.
        var f = new Fixture();
        ArrangeUnresolvedMasterSeries(f, out _);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Renamed", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsPreservingTimesAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.Title == "Renamed"),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchEventFieldsAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the ordinary patch would carry the local-row proxy as the series' new origin");

        // Nothing degraded and nothing was refused: the user asked for a rename and got a rename.
        // The COUNT here, not just the absence of a particular message — a Warning budget is only
        // enforceable if it is counted.
        f.Logger.Records.Where(r => r.Level == LogLevel.Warning).Should().BeEmpty(
            "a write that fully satisfies the request is an expected-and-handled condition, which the logging standard forbids reporting at Warning");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("duration")]
    [InlineData("all-day")]
    public async Task UpdateRecurringAsync_AllInSeries_MasterUnresolvableAndTheTimingChanged_ThrowsAndWritesNothing(string change)
    {
        // A real timing change makes the new origin a FUNCTION of the old one, and the old one is
        // exactly what is missing. There is no honest value to send, so nothing is sent.
        var f = new Fixture();
        ArrangeUnresolvedMasterSeries(f, out _);

        var request = change switch
        {
            "start" => new UpdateEventRequest("Weekly", InstanceStart.AddHours(-1), InstanceStart, false, "Loc", "Body"),
            "duration" => new UpdateEventRequest("Weekly", InstanceStart, InstanceStart.AddHours(2), false, "Loc", "Body"),
            _ => new UpdateEventRequest("Weekly", InstanceStart, InstanceStart.AddHours(1), true, "Loc", "Body")
        };

        var act = () => f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries);

        await act.Should().ThrowAsync<SeriesOriginUnresolvedException>();
        f.Google.Verify(g => g.PatchEventFieldsAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Google.Verify(g => g.PatchEventFieldsPreservingTimesAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);

        // One incident, one Warning — the refusal's own. The anchor site must not add a second, so
        // this asserts the count and not merely that the refusal was reported.
        f.Logger.Records.Where(r => r.Level == LogLevel.Warning).Should().ContainSingle()
            .Which.Message.Should().Contain("Refusing an all-in-series");
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_MasterUnresolvable_ContentHashDoesNotDependOnTheLocalRows()
    {
        // The content-hash is stamped into extendedProperties and recorded so Google's echo of this
        // write is recognised (FHQ-30) — so it must describe what was SENT. Start and end were not
        // sent, and the only start in scope is the proxy this whole ticket exists to distrust.
        // Two byte-identical edits differing only in which local row happens to be the earliest
        // synced one must therefore produce the SAME token.
        var early = new Fixture();
        ArrangeUnresolvedMasterSeries(early, out var earlyHash, earliestRowStart: InstanceStart.AddDays(-70));

        var late = new Fixture();
        ArrangeUnresolvedMasterSeries(late, out var lateHash, earliestRowStart: InstanceStart.AddDays(-7));

        await early.Sut.UpdateRecurringAsync(EventId, Req("Renamed", InstanceStart, "Body"), RecurrenceScope.AllInSeries);
        await late.Sut.UpdateRecurringAsync(EventId, Req("Renamed", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        earlyHash.Value.Should().NotBeNull();
        earlyHash.Value.Should().Be(lateHash.Value);
        early.Cache.Verify(c => c.Record(SeriesId, earlyHash.Value!), Times.Once,
            "the token recorded for echo matching must be the token that was sent");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSplitWithAnUnresolvableMaster_RefusesBeforeTouchingGoogle()
    {
        // The ordering half of the fix. The reshape (which needs the anchor) now runs BEFORE the
        // truncation, so a refusal leaves the original series intact. Reversed, the family lost the
        // tail of their series and got no replacement.
        var f = new Fixture();
        var split = ArrangeLadderSplit(f, storedZone: LondonZoneId, masterResolves: false);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().ThrowAsync<SeriesOriginUnresolvedException>();
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the original series must not be truncated by a split that then refuses");
        f.Google.Verify(g => g.CreateRecurringEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        split.Rule.Value.Should().BeNull();
        f.Repo.Verify(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSplitWithAnUnresolvableMaster_WarnsNamingTheSeriesAndTheCalendarById()
    {
        var f = new Fixture();
        ArrangeLadderSplit(f, storedZone: LondonZoneId, masterResolves: false);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().ThrowAsync<SeriesOriginUnresolvedException>();

        // One incident, one Warning (FHQ-161) — counted, not merely present. `Contain` cannot tell
        // one Warning from three, and the anchor site adding a second is precisely the regression
        // this assertion exists to catch.
        var warning = f.Logger.Records.Where(r => r.Level == LogLevel.Warning).Should().ContainSingle().Subject;
        warning.Message.Should().Contain("Refusing").And.Contain(SeriesId).And.Contain(AliceCalId.ToString());
        f.Logger.Records.Should().NotContain(r => r.Message.Contains(GoogleCalId),
            "a Google calendar id is an email address (FHQ-166)");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_UntilSplitWithAnUnresolvableMaster_IsUnaffected()
    {
        // A Never/UNTIL series has no count to derive, so it returns before the anchor is resolved
        // at all. The refusal must not spread to it: this split is still perfectly writable.
        var f = new Fixture();
        var split = ArrangeLadderSplit(
            f, storedZone: LondonZoneId, masterResolves: false,
            rrule: "RRULE:FREQ=WEEKLY;BYDAY=TU;UNTIL=20261201T000000Z");

        await f.Sut.UpdateRecurringAsync(EventId, Req("Football training", AutumnSplitStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Rule.Value.Should().Contain("UNTIL=20261201T000000Z");
        f.Google.Verify(g => g.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_MasterResolvable_StillPatchesWithStartAndEnd()
    {
        // Regression pin for the untouched majority: a resolvable master behaves exactly as before,
        // through the ordinary patch, carrying the shifted origin.
        var f = new Fixture();
        var masterStart = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var instance = f.RecurringInstance(EventId, "inst-3", InstanceStart);
        f.ArrangeEvent(instance);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", masterStart));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", masterStart)]);

        var newStart = InstanceStart.AddHours(-1);
        await f.Sut.UpdateRecurringAsync(
            EventId, new UpdateEventRequest("Weekly", newStart, newStart.AddHours(1), false, "Loc", "Body"),
            RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.Start == masterStart.AddHours(-1)),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchEventFieldsPreservingTimesAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Arranges an all-in-series edit of a series whose master Google will not resolve, with local
    /// rows reaching back only as far as <paramref name="earliestRowStart"/> — the proxy anchor.
    /// Captures the content-hash handed to the omit-start/end patch.
    /// </summary>
    private static void ArrangeUnresolvedMasterSeries(
        Fixture f, out StrongBox<string?> capturedHash, DateTimeOffset? earliestRowStart = null)
    {
        var instance = f.RecurringInstance(EventId, "inst-3", InstanceStart);
        f.ArrangeEvent(instance);

        var earliest = f.RecurringInstance(Guid.NewGuid(), "inst-1", earliestRowStart ?? InstanceStart.AddDays(-7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earliest, instance]);

        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeriesMaster?)null);

        var captured = new StrongBox<string?>(null);
        f.Google.Setup(g => g.PatchEventFieldsPreservingTimesAsync(
                GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CalendarEvent _, string h, CancellationToken _) => captured.Value = h)
            .Returns(Task.CompletedTask);

        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", InstanceStart)]);
        capturedHash = captured;
    }

    // ── FHQ-173: neither half of a split may commit on its own ────────────────────────────────
    //
    // A "this and following" edit is TWO independent Google mutations with nothing transactional
    // between them, so whichever runs first has already committed when the second fails. The ORDER
    // therefore decides what the family is left with:
    //   * truncate first (the shipped order) — the original series is chopped at the split with
    //     NOTHING replacing it. Every occurrence from the split onwards is gone, on every device the
    //     calendar is shared with, and the user is told only that their edit failed.
    //   * create first — a duplicate, overlapping series. Visible, non-destructive, and something
    //     the family can put right themselves in the Google Calendar app.
    // Neither is desirable; only one is recoverable, so the create goes first.
    //
    // A failed truncate is then compensated by deleting the series the create just made — but ONLY
    // when the failure proves Google never processed the truncation. The first version of this work
    // compensated unconditionally, on the argument that "if the compensating delete fails in turn we
    // land in the duplicate state, so compensation can never be worse than not compensating". That
    // enumerates only the delete FAILING. The delete SUCCEEDING against a truncation that actually
    // committed — Google applied it and lost the response to a 5xx or a dropped connection — leaves
    // the original series truncated with its replacement removed: the exact hole this ticket exists
    // to prevent, actively created rather than merely risked.
    //
    // Hence the rule these tests pin: under ambiguity, prefer the recoverable outcome. A duplicate
    // is visible and user-correctable; a hole is silent, permanent data loss on the system of record.
    //
    // ONE ERROR PER INCIDENT FROM THIS SERVICE (FHQ-161's rule, applied to Error). These tests assert
    // the COUNT of Error records, not merely that one is present. The successful-compensation case
    // asserts the absence of any Warning or Error at all: the calendar is back to where it started,
    // which the logging standard classes as expected-and-handled.

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ForwardSeriesCreateFails_LeavesTheOriginalSeriesUntruncated()
    {
        var f = new Fixture();
        var createFailure = new GoogleApiException(HttpStatusCode.ServiceUnavailable, "CreateRecurringEvent");
        var split = ArrangePartialWriteSplit(f, createFailure: createFailure);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Should().BeSameAs(createFailure);

        // The headline regression. In the shipped order the truncation had ALREADY committed by the
        // time the create was attempted, so this failure destroyed the tail of the family's series.
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a create that fails must not leave the original series truncated with nothing replacing it");
        split.Calls.Should().Equal(CreateCall);

        // There is nothing to compensate: the create is the write that failed, so no forward series
        // id exists. Compensation is for a failed truncate only.
        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Cache.Verify(c => c.Record(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        f.Repo.Verify(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ForwardSeriesCreateMayHaveBeenProcessed_ReportsThePossibleOrphanAtError()
    {
        // CreateRecurringEventAsync runs under RetryPolicy.RejectedOnly, so a 5xx is never repeated:
        // Google may have inserted the series and lost only the response. No id came back, so there
        // is nothing to delete and nothing to look it up by — the residual state can only be named.
        var f = new Fixture();
        ArrangePartialWriteSplit(f, createFailure: new GoogleApiException(HttpStatusCode.ServiceUnavailable, "CreateRecurringEvent"));

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().ThrowAsync<GoogleApiException>();

        var error = f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(SeriesId).And.Contain(AliceCalId.ToString());
        f.Logger.Records.Should().NotContain(r => r.Message.Contains(GoogleCalId),
            "a Google calendar id is an email address (FHQ-166)");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_ForwardSeriesCreateWasRejected_ReportsNothing()
    {
        // A 4xx means Google understood the request and refused it, so nothing was written and there
        // is no residual state at all. The exception the caller already gets IS the report; an Error
        // here would be a false alarm about an orphan that cannot exist.
        var f = new Fixture();
        ArrangePartialWriteSplit(f, createFailure: new GoogleApiException(HttpStatusCode.BadRequest, "CreateRecurringEvent"));

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        await act.Should().ThrowAsync<GoogleApiException>();

        f.Logger.Records.Should().NotContain(r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TruncateRejectedOutright_DeletesTheForwardSeriesItJustCreated()
    {
        // A 4xx truncate is positive evidence that Google did NOT process it, so the original series
        // is provably untouched and the forward series can be safely undone.
        var f = new Fixture();
        var truncateFailure = new GoogleApiException(HttpStatusCode.BadRequest, "PatchSeriesRecurrence");
        var split = ArrangePartialWriteSplit(f, truncateFailure: truncateFailure);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Should().BeSameAs(truncateFailure,
            "the caller is told about the write that actually failed, never about the clean-up");

        // Deleted by the id the CREATE returned — never one derived, guessed, or read back.
        f.Google.Verify(g => g.DeleteEventAsync(GoogleCalId, ForwardSeriesId, It.IsAny<CancellationToken>()), Times.Once);
        split.Calls.Should().Equal(CreateCall, TruncateCall, CompensateCall);

        // Compensated cleanly: the calendar is back to its pre-edit state, which is exactly what the
        // user has been told. That is an expected-and-handled outcome, not a degraded one.
        f.Logger.Records.Should().NotContain(r => r.Level >= LogLevel.Warning);
        f.Logger.Records.Should().ContainSingle(r => r.Level == LogLevel.Information)
            .Which.Message.Should().Contain(SeriesId).And.Contain(ForwardSeriesId);

        // The local rows survive because the remote series does. Pruning them here would delete the
        // family's occurrences locally to match a truncation that never happened.
        f.Repo.Verify(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TruncateMayHaveBeenProcessed_LeavesTheForwardSeriesInPlace()
    {
        // THE regression for the compensator's own failure mode. PatchSeriesRecurrenceAsync runs
        // under RetryPolicy.Full, where a 5xx is explicitly modelled as "may have been processed".
        // If Google applied the truncation and the response was lost, deleting the forward series
        // would leave the original chopped at the split with its replacement gone — a hole, created
        // by the clean-up. So: no delete. Leave the duplicate, report it, rethrow.
        var f = new Fixture();
        var truncateFailure = new GoogleApiException(HttpStatusCode.ServiceUnavailable, "PatchSeriesRecurrence");
        var split = ArrangePartialWriteSplit(f, truncateFailure: truncateFailure);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Should().BeSameAs(truncateFailure);

        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "undoing a write that may have committed is how the hole gets created");
        split.Calls.Should().Equal(CreateCall, TruncateCall);

        var error = f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(SeriesId).And.Contain(ForwardSeriesId).And.Contain(AliceCalId.ToString());
        f.Logger.Records.Should().NotContain(r => r.Message.Contains(GoogleCalId),
            "a Google calendar id is an email address (FHQ-166)");

        f.Repo.Verify(r => r.DeleteEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TruncateTimesOut_LeavesTheForwardSeriesInPlace()
    {
        // FHQ-91's per-attempt HttpClient timeout: a TaskCanceledException wrapping a
        // TimeoutException, with the CALLER'S TOKEN UNTOUCHED. TaskCanceledException derives from
        // OperationCanceledException, so a type test would call this a cancellation — it is not, and
        // DomainExceptionHandler maps it to 504, not 499. It skips compensation because the request
        // may have reached Google and been processed, which is a different rule reaching the same
        // answer for a different reason.
        var f = new Fixture();
        var timeout = new TaskCanceledException("attempt timed out", new TimeoutException());
        var split = ArrangePartialWriteSplit(f, truncateFailure: timeout);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<TaskCanceledException>()).Which.Should().BeSameAs(timeout);

        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a timed-out truncation may have been processed by Google anyway");
        split.Calls.Should().Equal(CreateCall, TruncateCall);
        f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle()
            .Which.Message.Should().Contain(ForwardSeriesId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TruncateFailsWithTheCallerCancelled_AttemptsNoCompensation()
    {
        // Genuine cancellation, established by the TOKEN rather than the exception type — here with
        // a truncation failure that would otherwise be compensated. There is no token left to write
        // with, and reaching for CancellationToken.None would issue a fresh write on behalf of an
        // abandoned request. The failure still reaches the caller: this path reports, never swallows.
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        var truncateFailure = new GoogleApiException(HttpStatusCode.BadRequest, "PatchSeriesRecurrence");
        var split = ArrangePartialWriteSplit(f, truncateFailure: truncateFailure, onTruncate: cts.Cancel);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing, cts.Token);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Should().BeSameAs(truncateFailure);

        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a cancelled token cannot carry a compensating write, and None would resurrect an abandoned request");
        split.Calls.Should().Equal(CreateCall, TruncateCall);
        f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle()
            .Which.Message.Should().Contain(ForwardSeriesId);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_CompensatingDeleteAlsoFails_ReportsBothSeriesAtError()
    {
        var f = new Fixture();
        var truncateFailure = new GoogleApiException(HttpStatusCode.BadRequest, "PatchSeriesRecurrence");
        ArrangePartialWriteSplit(
            f,
            truncateFailure: truncateFailure,
            compensationFailure: new GoogleApiException(HttpStatusCode.InternalServerError, "DeleteEvent"));

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<GoogleApiException>()).Which.Should().BeSameAs(truncateFailure,
            "a failed clean-up must not replace or mask the failure the user's edit actually hit");

        // The duplicate is the residual state an operator has to clean up, so it is named: both
        // series ids, and the calendar by FamilyHQ's own id.
        var error = f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(SeriesId).And.Contain(ForwardSeriesId).And.Contain(AliceCalId.ToString());
        f.Logger.Records.Should().NotContain(r => r.Message.Contains(GoogleCalId),
            "a Google calendar id is an email address (FHQ-166)");
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_BothWritesSucceed_CreatesTheForwardSeriesBeforeTruncatingTheOriginal()
    {
        var f = new Fixture();
        var split = ArrangePartialWriteSplit(f);

        await f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        split.Calls.Should().Equal(CreateCall, TruncateCall);
        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // The original's local rows at/after the split are pruned once BOTH writes have landed.
        f.Repo.Verify(r => r.DeleteEventAsync(split.AtSplit.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(split.After.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(split.Before.Id, It.IsAny<CancellationToken>()), Times.Never);

        // The series-rule map still carries BOTH series, so the reconcile stamps the truncated rule
        // on the original's surviving instances and the fresh rule on the forward series'.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-1"
                && e.RecurrenceRule != null && e.RecurrenceRule.Contains("UNTIL=")),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "fwd-inst-1"
                && e.RecurrenceRule != null && e.RecurrenceRule.Contains("FREQ=WEEKLY") && !e.RecurrenceRule.Contains("UNTIL=")),
            It.IsAny<CancellationToken>()), Times.Once);

        f.Logger.Records.Should().ContainSingle(r => r.Level == LogLevel.Information);
        f.Logger.Records.Should().NotContain(r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_TruncateNeedsReauth_PropagatesWithoutAttemptingCompensation()
    {
        // A reauth failure is "rejected, not processed" — the credentials never got past
        // authorisation — so the may-have-been-processed rule would allow the delete. It is skipped
        // for its own reason: the credentials ARE the failure, so the delete would be rejected
        // identically, buying nothing but a second failure to explain. The reauth still reaches the
        // caller, which is what puts the reconnect banner up.
        var f = new Fixture();
        var reauth = new GoogleReauthRequiredException(GoogleAuthFailureSource.CalendarApi, "invalid_grant");
        var split = ArrangePartialWriteSplit(f, truncateFailure: reauth);

        var act = () => f.Sut.UpdateRecurringAsync(
            EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        (await act.Should().ThrowAsync<GoogleReauthRequiredException>()).Which.Should().BeSameAs(reauth);

        f.Google.Verify(g => g.DeleteEventAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a delete on the credentials that just failed cannot succeed");
        split.Calls.Should().Equal(CreateCall, TruncateCall);

        // The forward series is still on Google, so the residual state IS the duplicate one and is
        // reported exactly as a failed compensating delete would report it.
        var error = f.Logger.Records.Where(r => r.Level == LogLevel.Error).Should().ContainSingle().Subject;
        error.Message.Should().Contain(SeriesId).And.Contain(ForwardSeriesId).And.Contain(AliceCalId.ToString());
    }

    private const string ForwardSeriesId = "new-series-id";
    private const string CreateCall = "create-forward-series";
    private const string TruncateCall = "truncate-original";
    private const string CompensateCall = "delete-forward-series";

    /// <summary>
    /// FHQ-173. Arranges an ordinary "this and following" split — resolvable master, weekly rule
    /// with no COUNT — and records the ORDER in which the two Google mutations and the compensating
    /// delete are made, so a test can state which of them happened and which did not. Each of the
    /// three can be made to fail independently.
    /// </summary>
    /// <param name="onTruncate">
    /// Runs as the truncate call is made, before it fails. Lets a test cancel the caller's token at
    /// the moment the second write goes out, which is when a real cancellation would land.
    /// </param>
    private static PartialWriteSplit ArrangePartialWriteSplit(
        Fixture f,
        Exception? createFailure = null,
        Exception? truncateFailure = null,
        Exception? compensationFailure = null,
        Action? onTruncate = null)
    {
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        var before  = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart.AddDays(-7));
        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        var after   = f.RecurringInstance(Guid.NewGuid(), "inst-3", InstanceStart.AddDays(7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([before, atSplit, after]);

        var calls = new List<string>();

        f.Google.Setup(g => g.CreateRecurringEventAsync(
                GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CalendarEvent e, string _, string _, CancellationToken _) =>
            {
                calls.Add(CreateCall);
                if (createFailure is not null)
                    return Task.FromException<CalendarEvent>(createFailure);

                e.GoogleEventId = ForwardSeriesId;
                return Task.FromResult(e);
            });

        f.Google.Setup(g => g.PatchSeriesRecurrenceAsync(
                GoogleCalId, SeriesId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, string _, CancellationToken _) =>
            {
                calls.Add(TruncateCall);
                onTruncate?.Invoke();
                return truncateFailure is null ? Task.CompletedTask : Task.FromException(truncateFailure);
            });

        f.Google.Setup(g => g.DeleteEventAsync(GoogleCalId, ForwardSeriesId, It.IsAny<CancellationToken>()))
            .Returns((string _, string _, CancellationToken _) =>
            {
                calls.Add(CompensateCall);
                return compensationFailure is null ? Task.CompletedTask : Task.FromException(compensationFailure);
            });

        // One surviving instance of the truncated original and one of the forward series, both
        // RRULE-less as GetEventsAsync (pass 1) really returns them.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("inst-1", InstanceStart.AddDays(-7)),
            f.GoogleInstanceNoRule("fwd-inst-1", InstanceStart, recurringId: ForwardSeriesId)
        ]);

        return new PartialWriteSplit(calls, before, atSplit, after);
    }

    private sealed record PartialWriteSplit(
        IReadOnlyList<string> Calls, CalendarEvent Before, CalendarEvent AtSplit, CalendarEvent After);

    [Fact]
    public async Task DeleteRecurringAsync_ThisAndFollowing_CountSeries_TruncatesWithUntil()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10";
        f.ArrangeEvent(instance);

        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([atSplit]);

        // Deleting "this and following" only truncates the original master to UNTIL = split - 1s.
        // No forward series is created, so a COUNT-bounded series no longer needs to be rejected.
        await f.Sut.DeleteRecurringAsync(EventId, RecurrenceScope.ThisAndFollowing);

        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(GoogleCalId, SeriesId,
            It.Is<string>(s => s.Contains("UNTIL=")), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AllInSeries edit ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_PatchesMasterAndPreservesExceptionOverrides()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);
        // FHQ-172: the master patch shape asserted below presupposes a RESOLVABLE master, which is
        // the Fixture's default — the degraded omit-start/end branch is opted into, never defaulted.

        // Reconcile window returns a normal instance and an exception (with overridden title + OriginalStartTime).
        var normal = f.GoogleInstance("inst-1", WindowStart.AddDays(7));
        var exception = f.GoogleInstance("inst-2", InstanceStart, isException: true);
        exception.Title = "Overridden Title";
        f.ArrangeReconcileWindow([normal, exception]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Series Title", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        // Writes the series master via events.patch (PATCH, merge semantics) — a full-resource replace
        // would omit the recurrence array and Google would collapse the series to a one-off event (FHQ-144).
        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // The exception row keeps its overridden title + OriginalStartTime after reconcile.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-2" && e.Title == "Overridden Title" && e.OriginalStartTime != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_SendsEditedFieldsOnTheMaster()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);
        // FHQ-172: a real time change needs the master's true origin, so state the anchor this test
        // depends on rather than inheriting it — it is anchored at the edited occurrence's own
        // start, which makes the shifted origin exactly the requested start.
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", InstanceStart));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", WindowStart.AddDays(7))]);

        var newStart = InstanceStart.AddHours(-1);
        var newEnd = newStart.AddHours(1);

        // Constructed directly rather than via the Req(...) helper, which pins End to Start + 1h.
        var request = new UpdateEventRequest("Gymnastics", newStart, newEnd, false, "Loc", "Body");

        await f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e =>
                e.GoogleEventId == SeriesId &&
                e.Title == "Gymnastics" &&
                e.Start == newStart &&
                e.End == newEnd),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_EditingLaterOccurrence_KeepsSeriesAnchoredToMasterStart()
    {
        // FHQ-144 follow-up: an AllInSeries edit arrives on ONE occurrence, but must not relocate the
        // series to that occurrence's date. The master patch shifts the master's DTSTART by the DELTA
        // the user applied to the edited occurrence — so the result depends on WHAT changed, not WHICH
        // occurrence was edited. Here occurrence 3 (Mar 15) has its time moved 09:00→08:00; the series
        // must stay anchored on the master's origin date (Mar 1) at the new time, NOT jump to Mar 15.
        var f = new Fixture();
        var masterStart = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);       // occurrence 1 (series origin)
        var editedOccurrenceStart = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero); // occurrence 3

        var instance = f.RecurringInstance(EventId, "inst-3", editedOccurrenceStart);
        f.ArrangeEvent(instance);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", masterStart));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", masterStart)]);

        // User changes only the TIME on occurrence 3: 09:00 → 08:00 (delta −1h), same date.
        var newStart = new DateTimeOffset(2026, 3, 15, 8, 0, 0, TimeSpan.Zero);
        var request = new UpdateEventRequest("Weekly", newStart, newStart.AddHours(1), false, "Loc", "Body");

        await f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries);

        // Master patch carries the ORIGIN date (Mar 1) with the new time (08:00), not the edited
        // occurrence's date (Mar 15).
        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e =>
                e.GoogleEventId == SeriesId &&
                e.Start == new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero) &&
                e.End == new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero)),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_UnchangedSave_DoesNotMoveTheSeries()
    {
        // Saving AllInSeries without changing the time on a later occurrence must be a no-op shift:
        // the master keeps its origin start exactly (delta = 0).
        var f = new Fixture();
        var masterStart = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        var editedOccurrenceStart = new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero);

        var instance = f.RecurringInstance(EventId, "inst-3", editedOccurrenceStart);
        f.ArrangeEvent(instance);
        f.Google.Setup(g => g.GetSeriesMasterAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", masterStart));
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-1", masterStart)]);

        // Request echoes the edited occurrence's own times unchanged (delta = 0).
        var request = new UpdateEventRequest("Weekly", editedOccurrenceStart, editedOccurrenceStart.AddHours(1), false, "Loc", "Body");

        await f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.Start == masterStart),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_RecordsEchoedMasterHashForEveryReconciledInstance()
    {
        const string MasterHash = "master-echoed-hash";
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        // Google copies the MASTER's content-hash onto every expanded instance. GetEventsAsync
        // surfaces that echoed value on CalendarEvent.ContentHash; the reconcile must record THAT
        // exact value (so the N webhook echoes match IsSelfEcho), not a per-instance recompute.
        var i1 = f.GoogleInstance("inst-1", WindowStart.AddDays(7)); i1.ContentHash = MasterHash;
        var i2 = f.GoogleInstance("inst-2", InstanceStart); i2.ContentHash = MasterHash;
        var i3 = f.GoogleInstance("inst-3", InstanceStart.AddDays(7)); i3.ContentHash = MasterHash;
        f.ArrangeReconcileWindow([i1, i2, i3]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("T", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        // The exact echoed master hash is recorded for each instance id.
        f.Cache.Verify(c => c.Record("inst-1", MasterHash), Times.Once);
        f.Cache.Verify(c => c.Record("inst-2", MasterHash), Times.Once);
        f.Cache.Verify(c => c.Record("inst-3", MasterHash), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_ExplicitTagNamingSharedCalendar_RetainsThatMember()
    {
        // FHQ-47 (Gap 2): the reconcile re-derives an instance's members from its description. When a
        // tagged calendar is TRANSIENTLY marked IsShared (the first-login auto-designation window), an
        // explicit "[members: ...]" tag naming it must still resolve it — the tag is authoritative and
        // resolves against ALL calendars, so the member is not silently dropped.
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        // Google returns an instance whose description explicitly tags Alice AND Family (Family is the
        // shared calendar — i.e. a calendar currently flagged IsShared=true).
        var fetched = f.GoogleInstance("inst-1", WindowStart.AddDays(7));
        fetched.Description = "Body\n[members: Alice, Family]";
        f.ArrangeReconcileWindow([fetched]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("T", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        // The reconciled row retains BOTH tagged members — Family is NOT dropped despite IsShared.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-1"
                && e.Members.Any(m => m.DisplayName == "Alice")
                && e.Members.Any(m => m.DisplayName == "Family")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_ThisAndFollowing_PersistsNewInstancesWithNonNullRecurrenceRule()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU";
        f.ArrangeEvent(instance);

        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-series-id"; return e; });

        // GetEventsAsync only does pass-1: the re-fetched instances carry a NULL RecurrenceRule.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("new-inst-1", InstanceStart, recurringId: "new-series-id"),
            f.GoogleInstanceNoRule("new-inst-2", InstanceStart.AddDays(7), recurringId: "new-series-id")
        ]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing);

        // The new series' instances must be persisted with the forward series' RRULE, not null —
        // otherwise a later recurring op on them throws "has no stored RecurrenceRule".
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "new-inst-1" && e.RecurrenceRule != null && e.RecurrenceRule.Contains("FREQ=WEEKLY")),
            It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "new-inst-2" && e.RecurrenceRule != null && e.RecurrenceRule.Contains("FREQ=WEEKLY")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_DoesNotClobberRuleOfOtherSeriesInWindow()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, SeriesId, InstanceStart);
        f.ArrangeEvent(instance);

        // An instance of a DIFFERENT series already stored locally with its own rule.
        var otherExisting = f.RecurringInstance(Guid.NewGuid(), "other-1", WindowStart.AddDays(3));
        otherExisting.GoogleRecurringEventId = "other-series";
        otherExisting.RecurrenceRule = "RRULE:FREQ=DAILY";
        f.Repo.Setup(r => r.GetEventByGoogleEventIdAsync("other-1", It.IsAny<CancellationToken>())).ReturnsAsync(otherExisting);

        // Window returns one instance of the reconciled series and one of the other series, both with null rule.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("inst-1", InstanceStart),
            f.GoogleInstanceNoRule("other-1", WindowStart.AddDays(3), recurringId: "other-series")
        ]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("T", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        // The other series' stored rule is preserved (not clobbered to the reconciled series' rule).
        f.Repo.Verify(r => r.UpdateEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "other-1" && e.RecurrenceRule == "RRULE:FREQ=DAILY"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Member-scope rule (§10.1.2) ───────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_MemberChangeAtThisOnly_IsRejected()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart); // current members: Alice
        f.ArrangeEvent(instance);

        // Request description carries a members tag adding Bob — a member-set change.
        var request = Req("T", InstanceStart, "Body\n[members: Alice, Bob]");

        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, request, RecurrenceScope.ThisOnly))
            .Should().ThrowAsync<MemberScopeViolationException>();
    }

    [Fact]
    public async Task UpdateRecurringAsync_PlainDescriptionWithoutTagAtThisOnly_IsAllowed()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart); // current members: Alice
        f.ArrangeEvent(instance);
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-2", InstanceStart, isException: true)]);

        // Plain text that happens to NAME a member but has no explicit [members:...] tag must NOT
        // be read as a member change (the whole-word fallback would spuriously reject this).
        var request = Req("T", InstanceStart, "Lunch with Bob and Alice");

        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, request, RecurrenceScope.ThisOnly))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateRecurringAsync_MemberChangeAtThisAndFollowing_IsRejected()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);
        var request = Req("T", InstanceStart, "Body\n[members: Alice, Bob]");

        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, request, RecurrenceScope.ThisAndFollowing))
            .Should().ThrowAsync<MemberScopeViolationException>();
    }

    [Fact]
    public async Task UpdateRecurringAsync_MemberChangeAtAllInSeries_IsAccepted()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, SeriesId, InstanceStart); // master row, members: Alice
        f.ArrangeEvent(instance);
        f.ArrangeReconcileWindow([f.GoogleInstance("inst-2", InstanceStart)]);

        // No 1↔N crossing here (still single member after parse, since Bob unknown -> stays Alice).
        var request = Req("T", InstanceStart, "Body");
        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateRecurringAsync_AllInSeries_MemberChangeCrossing1ToN_MigratesSeries()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart); // members: Alice (single)
        f.ArrangeEvent(instance);
        f.Migration.Setup(m => m.EnsureCorrectCalendarForSeriesAsync(SeriesId, It.IsAny<IReadOnlyList<CalendarInfo>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Description carries a members tag adding Bob → two members → crosses the 1↔N boundary.
        var request = Req("T", InstanceStart, "Body\n[members: Alice, Bob]");
        await f.Sut.UpdateRecurringAsync(EventId, request, RecurrenceScope.AllInSeries);

        // Migration is invoked; the plain master patch is NOT performed.
        f.Migration.Verify(m => m.EnsureCorrectCalendarForSeriesAsync(SeriesId,
            It.Is<IReadOnlyList<CalendarInfo>>(members => members.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchEventFieldsAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Delete scopes ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRecurringAsync_ThisOnly_DeletesInstanceAndRow()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        await f.Sut.DeleteRecurringAsync(EventId, RecurrenceScope.ThisOnly);

        f.Google.Verify(g => g.DeleteEventAsync(GoogleCalId, "inst-2", It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRecurringAsync_ThisAndFollowing_TruncatesMasterAndDeletesRowsFromSplit()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU";
        f.ArrangeEvent(instance);

        var before = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart.AddDays(-7));
        var atSplit = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        var after = f.RecurringInstance(Guid.NewGuid(), "inst-3", InstanceStart.AddDays(7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([before, atSplit, after]);

        await f.Sut.DeleteRecurringAsync(EventId, RecurrenceScope.ThisAndFollowing);

        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(GoogleCalId, SeriesId, It.Is<string>(s => s.Contains("UNTIL=")), It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(atSplit.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(after.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(before.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteRecurringAsync_AllInSeries_DeletesMasterAndAllSeriesRows()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        f.ArrangeEvent(instance);

        var i1 = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart.AddDays(-7));
        var i2 = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart);
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([i1, i2]);

        await f.Sut.DeleteRecurringAsync(EventId, RecurrenceScope.AllInSeries);

        f.Google.Verify(g => g.DeleteEventAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(i1.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(i2.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fail-fast on non-recurring ────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_NonRecurringEvent_FailsFast()
    {
        var f = new Fixture();
        var nonRecurring = new CalendarEvent
        {
            Id = EventId, GoogleEventId = "single", Title = "T",
            Start = InstanceStart, End = InstanceStart.AddHours(1),
            OwnerCalendarInfoId = AliceCalId, Members = [f.Alice]
        };
        f.ArrangeEvent(nonRecurring);

        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, Req("T", InstanceStart, "B"), RecurrenceScope.ThisOnly))
            .Should().ThrowAsync<NotPartOfRecurringSeriesException>();
    }

    [Fact]
    public async Task DeleteRecurringAsync_NonRecurringEvent_FailsFast()
    {
        var f = new Fixture();
        var nonRecurring = new CalendarEvent
        {
            Id = EventId, GoogleEventId = "single", Title = "T",
            Start = InstanceStart, End = InstanceStart.AddHours(1),
            OwnerCalendarInfoId = AliceCalId, Members = [f.Alice]
        };
        f.ArrangeEvent(nonRecurring);

        await f.Sut.Invoking(s => s.DeleteRecurringAsync(EventId, RecurrenceScope.ThisOnly))
            .Should().ThrowAsync<NotPartOfRecurringSeriesException>();
    }

    // ── Native recurring creation (FHQ-18.5 Part A) ───────────────────────────

    [Fact]
    public async Task CreateAsync_WithRecurrenceRule_CreatesSeriesMasterAndReconcilesWindow()
    {
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("new-master", InstanceStart, recurringId: "new-master"),
            f.GoogleInstanceNoRule("inst-2", InstanceStart.AddDays(7), recurringId: "new-master")
        ]);

        var request = CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU");
        await f.Sut.CreateAsync(request);

        // The series master is created with the RRULE in the recurrence array, not a single event.
        f.Google.Verify(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(),
            It.Is<string>(r => r.Contains("FREQ=WEEKLY")), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.CreateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithRecurrenceRule_PersistsInstancesWithSeriesIdAndRule()
    {
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });
        // GetEventsAsync is pass-1 only → instances carry a null RecurrenceRule; the reconcile must
        // stamp the new series' RRULE so they are not persisted RRULE-less.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("inst-1", InstanceStart, recurringId: "new-master"),
            f.GoogleInstanceNoRule("inst-2", InstanceStart.AddDays(7), recurringId: "new-master")
        ]);

        await f.Sut.CreateAsync(CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU"));

        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-1" && e.GoogleRecurringEventId == "new-master"
                && e.RecurrenceRule != null && e.RecurrenceRule.Contains("FREQ=WEEKLY")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithRecurrenceRule_RecordsEchoedHashForEveryInstance()
    {
        const string MasterHash = "create-master-hash";
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });
        var i1 = f.GoogleInstanceNoRule("inst-1", InstanceStart, recurringId: "new-master"); i1.ContentHash = MasterHash;
        var i2 = f.GoogleInstanceNoRule("inst-2", InstanceStart.AddDays(7), recurringId: "new-master"); i2.ContentHash = MasterHash;
        f.ArrangeReconcileWindow([i1, i2]);

        await f.Sut.CreateAsync(CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU"));

        f.Cache.Verify(c => c.Record("inst-1", MasterHash), Times.Once);
        f.Cache.Verify(c => c.Record("inst-2", MasterHash), Times.Once);
    }

    // ── FHQ-66: concurrent-sync write race on the recurring-create reconcile ──────

    [Fact]
    public async Task CreateAsync_RecurringSeries_WhenConcurrentSyncInsertsSameInstances_ReResolvesAndDoesNotThrow()
    {
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("inst-1", InstanceStart, recurringId: "new-master"),
            f.GoogleInstanceNoRule("inst-2", InstanceStart.AddDays(7), recurringId: "new-master")
        ]);

        // The reconcile's first SaveChanges hits the GoogleEventId unique index because a concurrent
        // CalendarSyncWorker inserted the same instances first (FHQ-66). The reconcile must re-resolve
        // its inserts against the now-stored rows (first-writer-wins) and retry — NOT surface the
        // DbUpdateException as an HTTP 500.
        f.Repo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "23505: duplicate key value violates unique constraint \"IX_Events_GoogleEventId\""))
            .ReturnsAsync(2);

        // After the conflict, the rows the concurrent sync inserted are now found by GoogleEventId
        // (null on the first pass through the reconcile loop, present on the re-resolve pass).
        var stored1 = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart);
        var stored2 = f.RecurringInstance(Guid.NewGuid(), "inst-2", InstanceStart.AddDays(7));
        f.Repo.SetupSequence(r => r.GetEventByGoogleEventIdAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null).ReturnsAsync(stored1);
        f.Repo.SetupSequence(r => r.GetEventByGoogleEventIdAsync("inst-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null).ReturnsAsync(stored2);

        var request = CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU");

        var act = async () => await f.Sut.CreateAsync(request);

        await act.Should().NotThrowAsync();
        // initial save (threw) + one retry after re-resolving the conflicting inserts.
        f.Repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        // The conflicting inserts are detached and converted to updates of the concurrently-stored rows.
        f.Repo.Verify(r => r.DetachEventAsync(It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-1"), It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.UpdateEventAsync(It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RecurringSeries_WhenConcurrentSyncInsertsSameInstances_DoesNotBlankTheStoredZone()
    {
        // The re-resolve folds this operation's fields onto the row the concurrent sync stored. That
        // row may already carry the zone Google reported for the series; an insert that carries none
        // (or a blank one) must not overwrite it, or the race would quietly cost the series its anchor.
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });

        var fetched = f.GoogleInstanceNoRule("inst-1", InstanceStart, recurringId: "new-master");
        fetched.IanaTimeZone = "";
        f.ArrangeReconcileWindow([fetched]);

        f.Repo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("23505: duplicate key value violates unique constraint \"IX_Events_GoogleEventId\""))
            .ReturnsAsync(1);

        var stored = f.RecurringInstance(Guid.NewGuid(), "inst-1", InstanceStart);
        stored.IanaTimeZone = LondonZoneId;
        f.Repo.SetupSequence(r => r.GetEventByGoogleEventIdAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null).ReturnsAsync(stored);

        await f.Sut.CreateAsync(CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU"));

        stored.IanaTimeZone.Should().Be(LondonZoneId);
    }

    [Fact]
    public async Task CreateAsync_RecurringSeries_WhenReconcileFailsTwice_PropagatesTheError()
    {
        var f = new Fixture();
        f.Google.Setup(g => g.CreateRecurringEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, string _, CancellationToken _) => { e.GoogleEventId = "new-master"; return e; });
        f.ArrangeReconcileWindow([f.GoogleInstanceNoRule("inst-1", InstanceStart, recurringId: "new-master")]);

        // A genuine, non-transient save failure must not be swallowed: the retry also fails, so the
        // exception propagates rather than being masked by the FHQ-66 race handling.
        f.Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("persistent failure"));

        var request = CreateReq([AliceCalId], "Standup", InstanceStart, "Body", "RRULE:FREQ=WEEKLY;BYDAY=SU");

        await f.Sut.Invoking(s => s.CreateAsync(request)).Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task CreateAsync_WithoutRecurrenceRule_CreatesSingleEvent()
    {
        var f = new Fixture();
        f.Google.Setup(g => g.CreateEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => { e.GoogleEventId = "single"; return e; });

        await f.Sut.CreateAsync(CreateReq([AliceCalId], "Once", InstanceStart, "Body", recurrenceRule: null));

        f.Google.Verify(g => g.CreateEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.CreateRecurringEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Recurrence toggle ON / OFF (FHQ-18.5 Part A) ──────────────────────────

    [Fact]
    public async Task UpdateAsync_RecurrenceOnForNonRecurringEvent_PromotesToSeriesAndReconciles()
    {
        var f = new Fixture();
        var single = new CalendarEvent
        {
            Id = EventId, GoogleEventId = "single", Title = "Lunch",
            Start = InstanceStart, End = InstanceStart.AddHours(1),
            Description = "Body\n[members: Alice]",
            OwnerCalendarInfoId = AliceCalId, Members = [f.Alice]
        };
        f.ArrangeEvent(single);
        // After promotion Google expands the now-series into COMPOUND-id instances whose
        // GoogleRecurringEventId is the master (== the original single id). The original single id is
        // NOT among them — Google replaces the single event with the expanded series.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("single_20260315T090000Z", InstanceStart, recurringId: "single"),
            f.GoogleInstanceNoRule("single_20260322T090000Z", InstanceStart.AddDays(7), recurringId: "single")
        ]);

        var request = ReqRecurrence("Lunch", InstanceStart, "Body", recurrenceRule: "RRULE:FREQ=WEEKLY;BYDAY=SU", clear: false);
        var result = await f.Sut.UpdateAsync(EventId, request);

        // Promote in place: patch the recurrence array onto the event's own id, then reconcile.
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(GoogleCalId, "single",
            It.Is<string>(r => r.Contains("FREQ=WEEKLY")), It.IsAny<CancellationToken>()), Times.Once);
        // The expanded instances are persisted with the RRULE and the series link.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "single_20260322T090000Z" && e.GoogleRecurringEventId == "single"
                && e.RecurrenceRule != null && e.RecurrenceRule.Contains("FREQ=WEEKLY")),
            It.IsAny<CancellationToken>()), Times.Once);
        // The return value is a recurring row from the reconciled set, not the stale single.
        result.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_RecurrenceOn_RemovesStaleOriginalSingleRow()
    {
        var f = new Fixture();
        var single = new CalendarEvent
        {
            Id = EventId, GoogleEventId = "single", Title = "Lunch",
            Start = InstanceStart, End = InstanceStart.AddHours(1),
            Description = "Body\n[members: Alice]",
            OwnerCalendarInfoId = AliceCalId, Members = [f.Alice]
        };
        f.ArrangeEvent(single);
        // Google's expansion uses compound ids; the original "single" row is left behind as a
        // non-recurring duplicate unless the toggle deletes it after the reconcile.
        f.ArrangeReconcileWindow([
            f.GoogleInstanceNoRule("single_20260315T090000Z", InstanceStart, recurringId: "single"),
            f.GoogleInstanceNoRule("single_20260322T090000Z", InstanceStart.AddDays(7), recurringId: "single")
        ]);

        var request = ReqRecurrence("Lunch", InstanceStart, "Body", recurrenceRule: "RRULE:FREQ=WEEKLY;BYDAY=SU", clear: false);
        await f.Sut.UpdateAsync(EventId, request);

        // The original non-recurring row is deleted so it is not orphaned as a stale duplicate.
        f.Repo.Verify(r => r.DeleteEventAsync(EventId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RecurrenceOffForRecurringEvent_CollapsesViaReconcileAndDeletesInstanceRows()
    {
        var f = new Fixture();
        // The toggled event is a COMPOUND-id instance row — its GoogleEventId is NEVER equal to the
        // series/master id. This is the real production shape (singleEvents=true expansion).
        var instance = f.RecurringInstance(EventId, $"{SeriesId}_20260315T090000Z", InstanceStart);
        f.ArrangeEvent(instance);

        // No local row's GoogleEventId equals the master id (the real case). All three rows are
        // expanded instances of the series.
        var inst1 = f.RecurringInstance(Guid.NewGuid(), $"{SeriesId}_20260308T090000Z", InstanceStart.AddDays(-7));
        var inst3 = f.RecurringInstance(Guid.NewGuid(), $"{SeriesId}_20260322T090000Z", InstanceStart.AddDays(7));
        f.Repo.Setup(r => r.GetEventsBySeriesIdAsync(SeriesId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([instance, inst1, inst3]);

        // After clearing recurrence, Google returns ONE single event whose id == the master/series id,
        // with no recurringEventId and no RRULE. The reconcile upserts it as a clean single row.
        var collapsed = f.GoogleInstanceNoRule(SeriesId, InstanceStart, recurringId: null);
        collapsed.GoogleRecurringEventId = null;
        collapsed.ContentHash = "collapsed-hash";
        f.ArrangeReconcileWindow([collapsed]);

        var request = ReqRecurrence("Weekly", InstanceStart, "Body", recurrenceRule: null, clear: true);
        var result = await f.Sut.UpdateAsync(EventId, request);

        // The series recurrence is cleared on Google (empty recurrence array collapses the series).
        f.Google.Verify(g => g.ClearSeriesRecurrenceAsync(GoogleCalId, SeriesId, It.IsAny<CancellationToken>()), Times.Once);
        // The collapsed single event is upserted as a clean non-recurring row by the reconcile.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId && e.GoogleRecurringEventId == null && e.RecurrenceRule == null),
            It.IsAny<CancellationToken>()), Times.Once);
        // Every expanded instance row is deleted (none survived the reconcile — collapsed id differs).
        f.Repo.Verify(r => r.DeleteEventAsync(instance.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(inst1.Id, It.IsAny<CancellationToken>()), Times.Once);
        f.Repo.Verify(r => r.DeleteEventAsync(inst3.Id, It.IsAny<CancellationToken>()), Times.Once);
        // The echoed collapsed-event hash is recorded via the reconcile (no bypass of the guard).
        f.Cache.Verify(c => c.Record(SeriesId, "collapsed-hash"), Times.Once);
        // The returned row is the clean single (not recurring).
        result.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_NoRecurrenceChange_KeepsLegacySingleEventBehaviour()
    {
        var f = new Fixture();
        var single = new CalendarEvent
        {
            Id = EventId, GoogleEventId = "single", Title = "Lunch",
            Start = InstanceStart, End = InstanceStart.AddHours(1),
            Description = "Body\n[members: Alice]",
            OwnerCalendarInfoId = AliceCalId, Members = [f.Alice]
        };
        f.ArrangeEvent(single);

        await f.Sut.UpdateAsync(EventId, Req("Lunch", InstanceStart, "Body"));

        // Plain field update via PatchEventFieldsAsync; no recurrence calls.
        f.Google.Verify(g => g.PatchEventFieldsAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Google.Verify(g => g.ClearSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── IDOR guard (recurring) ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringAsync_WithOtherUsersEventId_ThrowsEventNotFoundException()
    {
        var f = new Fixture();
        f.Repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        var request = Req("T", InstanceStart, "Body");
        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, request, RecurrenceScope.ThisOnly))
                   .Should().ThrowAsync<EventNotFoundException>();
    }

    [Fact]
    public async Task DeleteRecurringAsync_WithOtherUsersEventId_ThrowsEventNotFoundException()
    {
        var f = new Fixture();
        f.Repo.Setup(r => r.GetEventAsync(EventId, "u-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarEvent?)null);

        await f.Sut.Invoking(s => s.DeleteRecurringAsync(EventId, RecurrenceScope.ThisOnly))
                   .Should().ThrowAsync<EventNotFoundException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UpdateEventRequest Req(string title, DateTimeOffset start, string? description, bool isAllDay = false) =>
        new(title, start, start.AddHours(1), isAllDay, "Loc", description);

    private static UpdateEventRequest ReqRecurrence(string title, DateTimeOffset start, string? description, string? recurrenceRule, bool clear) =>
        new(title, start, start.AddHours(1), false, "Loc", description, recurrenceRule, clear);

    private static CreateEventRequest CreateReq(IReadOnlyList<Guid> members, string title, DateTimeOffset start, string? description, string? recurrenceRule) =>
        new(members, title, start, start.AddHours(1), false, "Loc", description, recurrenceRule);

    private sealed class Fixture
    {
        public readonly Mock<IGoogleCalendarClient> Google = new();
        public readonly Mock<ICalendarRepository> Repo = new();
        public readonly Mock<ICalendarMigrationService> Migration = new();
        public readonly Mock<IMemberTagParser> TagParser = new();
        public readonly Mock<IOutboundWriteHashCache> Cache = new();
        public readonly Mock<ICurrentUserService> CurrentUser = new();
        public readonly RecordingLogger<CalendarEventService> Logger = new();
        public readonly CalendarEventService Sut;

        public readonly CalendarInfo Alice = new() { Id = AliceCalId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice" };
        public readonly CalendarInfo Bob = new() { Id = BobCalId, GoogleCalendarId = "bob@google.com", DisplayName = "Bob" };
        public readonly CalendarInfo Shared = new() { Id = SharedCalId, GoogleCalendarId = SharedGoogleCalId, DisplayName = "Family", IsShared = true };

        public Fixture()
        {
            CurrentUser.SetupGet(u => u.UserId).Returns("u-1");

            // Real parser: exercises the actual members-tag normalise/parse logic (no behaviour to mock).
            var realParser = new MemberTagParser();
            TagParser.Setup(p => p.NormaliseDescription(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names) => realParser.NormaliseDescription(d, names));
            TagParser.Setup(p => p.StripMemberTag(It.IsAny<string>()))
                .Returns((string d) => realParser.StripMemberTag(d));
            TagParser.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names, IReadOnlyList<string>? tagged) => realParser.ParseMembers(d, names, tagged));
            TagParser.Setup(p => p.ExtractTaggedMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names) => realParser.ExtractTaggedMembers(d, names));

            Repo.Setup(r => r.GetCalendarsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([Alice, Bob, Shared]);
            Repo.Setup(r => r.GetCalendarByIdAsync(AliceCalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Alice);
            Repo.Setup(r => r.GetSyncStateAsync(AliceCalId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SyncState { CalendarInfoId = AliceCalId, SyncWindowStart = WindowStart, SyncWindowEnd = WindowEnd });
            Repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Repo.Setup(r => r.GetEventByGoogleEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CalendarEvent?)null);
            Repo.Setup(r => r.GetEventsBySeriesIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Google.Setup(g => g.PatchEventFieldsAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);

            // FHQ-172: default to a RESOLVABLE series master, because that is production's
            // overwhelming majority. Moq's own default is null, which this service reads as "the
            // series' true origin is unknown" and routes down the degraded omit-start/end branch —
            // so before this default, every test that simply forgot a setup was silently exercising
            // the rare path while appearing to test the normal one. That mock-default trap is the
            // same defect class that let the original bug ship, and it is fixed once here rather
            // than by remembering to add a line to each new test. A test that WANTS the degraded
            // path overrides this with an explicit null, which then reads as a deliberate choice.
            Google.Setup(g => g.GetSeriesMasterAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeriesMaster("RRULE:FREQ=WEEKLY;BYDAY=SU", InstanceStart));

            // Real zone factory: a tzdb lookup is pure, deterministic computation (no I/O, no clock),
            // and substituting it would mean asserting DST behaviour against fake DST rules.
            Sut = new CalendarEventService(Google.Object, Repo.Object, Migration.Object, TagParser.Object, Cache.Object,
                CurrentUser.Object, new NodaTimeRecurrenceTimeZoneFactory(), Logger);
        }

        public CalendarEvent RecurringInstance(Guid id, string googleEventId, DateTimeOffset start) => new()
        {
            Id = id,
            GoogleEventId = googleEventId,
            Title = "Weekly",
            Start = start,
            End = start.AddHours(1),
            Description = "Body\n[members: Alice]",
            OwnerCalendarInfoId = AliceCalId,
            GoogleRecurringEventId = SeriesId,
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU",
            Members = [Alice]
        };

        public CalendarEvent GoogleInstance(string googleEventId, DateTimeOffset start, bool isException = false, string? recurringId = null) => new()
        {
            GoogleEventId = googleEventId,
            Title = "Weekly",
            Start = start,
            End = start.AddHours(1),
            Description = "Body\n[members: Alice]",
            GoogleRecurringEventId = recurringId ?? SeriesId,
            OriginalStartTime = isException ? start : null,
            RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU"
        };

        // Mirrors what GetEventsAsync actually returns: pass-1 only, so RecurrenceRule is null.
        public CalendarEvent GoogleInstanceNoRule(string googleEventId, DateTimeOffset start, bool isException = false, string? recurringId = null)
        {
            var evt = GoogleInstance(googleEventId, start, isException, recurringId);
            evt.RecurrenceRule = null;
            return evt;
        }

        public void ArrangeEvent(CalendarEvent evt) =>
            Repo.Setup(r => r.GetEventAsync(evt.Id, "u-1", It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        // Make the reconcile's GetEventByGoogleEventIdAsync return an already-stored row so the
        // upsert takes the UPDATE branch rather than ADD.
        public void ArrangeExistingRow(CalendarEvent evt) =>
            Repo.Setup(r => r.GetEventByGoogleEventIdAsync(evt.GoogleEventId, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

        // The reconcile re-fetches the owner calendar's window from Google; arrange the returned instances.
        public void ArrangeReconcileWindow(IReadOnlyList<CalendarEvent> instances) =>
            Google.Setup(g => g.GetEventsAsync(GoogleCalId, WindowStart, WindowEnd, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((instances, (string?)null));
    }
}
