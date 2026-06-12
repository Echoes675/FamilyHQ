using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Exceptions;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
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
        f.Google.Verify(g => g.UpdateEventAsync(GoogleCalId,
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

        // Exactly TWO Google writes: truncate the original master, then insert the new series.
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

        // Reconcile window returns a normal instance and an exception (with overridden title + OriginalStartTime).
        var normal = f.GoogleInstance("inst-1", WindowStart.AddDays(7));
        var exception = f.GoogleInstance("inst-2", InstanceStart, isException: true);
        exception.Title = "Overridden Title";
        f.ArrangeReconcileWindow([normal, exception]);

        await f.Sut.UpdateRecurringAsync(EventId, Req("Series Title", InstanceStart, "Body"), RecurrenceScope.AllInSeries);

        // Patches the series master.
        f.Google.Verify(g => g.UpdateEventAsync(GoogleCalId,
            It.Is<CalendarEvent>(e => e.GoogleEventId == SeriesId), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        // The exception row keeps its overridden title + OriginalStartTime after reconcile.
        f.Repo.Verify(r => r.AddEventAsync(
            It.Is<CalendarEvent>(e => e.GoogleEventId == "inst-2" && e.Title == "Overridden Title" && e.OriginalStartTime != null),
            It.IsAny<CancellationToken>()), Times.Once);
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
        f.Google.Verify(g => g.UpdateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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

        // Plain field update via UpdateEventAsync; no recurrence calls.
        f.Google.Verify(g => g.UpdateEventAsync(GoogleCalId, It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Google.Verify(g => g.PatchSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        f.Google.Verify(g => g.ClearSeriesRecurrenceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UpdateEventRequest Req(string title, DateTimeOffset start, string? description) =>
        new(title, start, start.AddHours(1), false, "Loc", description);

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
        public readonly CalendarEventService Sut;

        public readonly CalendarInfo Alice = new() { Id = AliceCalId, GoogleCalendarId = GoogleCalId, DisplayName = "Alice" };
        public readonly CalendarInfo Bob = new() { Id = BobCalId, GoogleCalendarId = "bob@google.com", DisplayName = "Bob" };
        public readonly CalendarInfo Shared = new() { Id = SharedCalId, GoogleCalendarId = SharedGoogleCalId, DisplayName = "Family", IsShared = true };

        public Fixture()
        {
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

            Google.Setup(g => g.UpdateEventAsync(It.IsAny<string>(), It.IsAny<CalendarEvent>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, CalendarEvent e, string _, CancellationToken _) => e);

            Sut = new CalendarEventService(Google.Object, Repo.Object, Migration.Object, TagParser.Object, Cache.Object,
                new Mock<ILogger<CalendarEventService>>().Object);
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
            Repo.Setup(r => r.GetEventAsync(evt.Id, It.IsAny<CancellationToken>())).ReturnsAsync(evt);

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
