using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FluentAssertions;
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
    public async Task UpdateRecurringAsync_ThisAndFollowing_CountSeries_ThrowsDocumentedLimitation()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10";
        f.ArrangeEvent(instance);

        await f.Sut.Invoking(s => s.UpdateRecurringAsync(EventId, Req("Updated", InstanceStart, "Body"), RecurrenceScope.ThisAndFollowing))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*COUNT*FHQ-18.5*");
    }

    [Fact]
    public async Task DeleteRecurringAsync_ThisAndFollowing_CountSeries_ThrowsDocumentedLimitation()
    {
        var f = new Fixture();
        var instance = f.RecurringInstance(EventId, "inst-2", InstanceStart);
        instance.RecurrenceRule = "RRULE:FREQ=WEEKLY;BYDAY=SU;COUNT=10";
        f.ArrangeEvent(instance);

        await f.Sut.Invoking(s => s.DeleteRecurringAsync(EventId, RecurrenceScope.ThisAndFollowing))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*COUNT*FHQ-18.5*");
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
            .Should().ThrowAsync<InvalidOperationException>();
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
            .Should().ThrowAsync<InvalidOperationException>();
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
            .Should().ThrowAsync<InvalidOperationException>();
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
            .Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static UpdateEventRequest Req(string title, DateTimeOffset start, string? description) =>
        new(title, start, start.AddHours(1), false, "Loc", description);

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
            TagParser.Setup(p => p.ParseMembers(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns((string d, IReadOnlyList<string> names) => realParser.ParseMembers(d, names));
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
