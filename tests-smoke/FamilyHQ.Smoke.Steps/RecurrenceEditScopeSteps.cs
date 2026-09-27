using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RK5, RK6 and RK7 — the three scopes an edit can be applied at, each of which reaches Google a different
/// way.
/// <para>
/// Every assertion reads from Google. The three routes are only distinguishable there: an exception is a
/// separate event resource carrying the slot it came from, a split is a truncated rule plus a second
/// master, and a whole-series change is a patched master. FamilyHQ's own view of its own write shows none
/// of that.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceEditScopeSteps(ScenarioContext scenarioContext)
{
    /// <summary>
    /// The occurrence an edit is applied to. The middle one on purpose: it leaves an occurrence before it
    /// and an occurrence after it, so "only this one changed", "everything from here changed" and
    /// "everything changed" are three visibly different outcomes rather than two.
    /// </summary>
    private const int MiddleOccurrence = 1;

    private readonly List<DateTimeOffset> _originalStarts = [];

    private string? _originalMasterId;
    private string? _originalRule;
    private string? _originalTitle;
    private string? _overrideTitle;
    private DateTimeOffset? _overrideStart;
    private DateTimeOffset? _splitStart;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    // ── Renaming one occurrence, at a chosen scope ──────────────────────────────

    [When(@"I rename the middle occurrence on the kiosk for this event only")]
    public Task WhenIRenameTheMiddleOccurrenceForThisEventOnly() =>
        RenameOccurrenceAsync(MiddleOccurrence, "Singled-out occurrence", SmokeRecurrenceScope.ThisEvent);

    [When(@"I rename the middle occurrence on the kiosk for this and following events")]
    public Task WhenIRenameTheMiddleOccurrenceForThisAndFollowing() =>
        RenameOccurrenceAsync(MiddleOccurrence, "Series from the split", SmokeRecurrenceScope.ThisAndFollowing);

    [When(@"I rename the middle occurrence on the kiosk for all events")]
    public Task WhenIRenameTheMiddleOccurrenceForAllEvents() =>
        RenameOccurrenceAsync(MiddleOccurrence, "Renamed for all events", SmokeRecurrenceScope.AllEvents);

    /// <summary>
    /// RK7's precondition, and the half that has to happen first: an occurrence singled out and changed on
    /// its own, so that the whole-series change which follows is made over an existing exception rather than
    /// over a plain series. A failure here is the precondition, not the behaviour under test, and it says so.
    /// </summary>
    [Given(@"the first occurrence has already been renamed on the kiosk for this event only")]
    public async Task GivenTheFirstOccurrenceHasAlreadyBeenRenamedForThisEventOnly()
    {
        _overrideTitle = await RenameOccurrenceAsync(
            0, "Occurrence with its own title", SmokeRecurrenceScope.ThisEvent);
        _overrideStart = _splitStart;

        var exceptions = await SmokeSeries.ExceptionsAsync(State);

        exceptions.Should().ContainSingle(
            "this scenario is about what a whole-series change does to an occurrence that was already "
            + "singled out, so the single-occurrence change has to have produced one exception before the "
            + "series change is made. None here means the precondition failed, not the behaviour under test")
            .Which.Summary.Should().Be(
                _overrideTitle,
                "the exception has to be carrying the override title before the series change, or what "
                + "follows would be an ordinary whole-series edit wearing this scenario's name");
    }

    // ── RK5: this event only ────────────────────────────────────────────────────

    [Then(@"Google records that occurrence as an exception against its original slot")]
    public async Task ThenGoogleRecordsThatOccurrenceAsAnExceptionAgainstItsOriginalSlot()
    {
        var state = State;

        var exceptions = await ExceptionsSettledAsync(state, 1, "the occurrence that was singled out");
        var exception = exceptions.Single();

        exception.Summary.Should().Be(
            state.ExpectedTitle,
            "the occurrence the user edited is the one that must carry the new title");

        exception.RecurringEventId.Should().Be(
            _originalMasterId,
            "an exception belongs to its series. An event with no link back to the master is a separate "
            + "event that happens to sit on the same day, and the family would see two");

        SmokeSeries.OriginalSlotOf(exception).Should().Be(
            _splitStart!.Value,
            "Google records which slot of the series an exception replaces. Without the original slot the "
            + "series would expand its own occurrence there as well, and the family would see both");

        var master = await SmokeSeries.MasterEventAsync(state);

        master.Summary.Should().Be(
            _originalTitle,
            "a change asked for at 'this event' must leave the master alone. A master that took the new "
            + "title renames every other occurrence too — the outcome the user explicitly did not choose");

        SmokeSeries.RequireSingleRule(master).Should().Be(
            _originalRule, "editing one occurrence changes no rule");
    }

    // ── RK6: this and following ─────────────────────────────────────────────────

    [Then(@"Google holds the original series ending before the split and a replacement from it")]
    public async Task ThenGoogleHoldsTheOriginalSeriesEndingBeforeTheSplitAndAReplacementFromIt()
    {
        var state = State;
        var masters = await SmokeSeries.RecordMastersAsync(
            state, 2, "the two halves a 'this and following' change leaves behind");

        var original = masters.SingleOrDefault(
            candidate => candidate.Event.Id == _originalMasterId);

        original.Should().NotBeNull(
            "the original series must still exist, truncated. A 'this and following' change that removed it "
            + "would take the occurrences before the split with it, and those are the family's history");

        var replacement = masters.Single(candidate => candidate.Event.Id != _originalMasterId);

        SmokeSeries.RequireSingleRule(original!.Event).Should().Contain(
            "UNTIL=",
            "the original series has to end just before the split. A rule left as it was means the two "
            + "series overlap from the split point and the family sees every later occurrence twice");

        SmokeSeries.RequireSingleRule(original.Event).Should().NotContain(
            "COUNT=",
            "a truncated series is bounded by a date, not by a count — two end conditions on one rule is "
            + "not a rule, and which of them Google honoured would be a guess");

        replacement.Event.Summary.Should().Be(
            state.ExpectedTitle, "the replacement series is what carries the change the user asked for");

        SmokeSeries.StartOf(replacement.Event).Should().Be(
            _splitStart!.Value,
            "the replacement must start at the occurrence the user split on. Starting anywhere else either "
            + "loses that occurrence or duplicates the one before it");

        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Select(SmokeSeries.StartOf).Should().Equal(
            _originalStarts,
            "a split rearranges which series owns which occurrence; it does not move, add or drop one. The "
            + "family asked to change a title from a date onwards, not to have their calendar re-timed");
    }

    // ── RK7: all events, over an existing override ──────────────────────────────

    /// <summary>
    /// What the whole-series change must leave of the occurrence that was singled out: it is still <b>one</b>
    /// exception, still pinned to the slot it replaces.
    /// <para>
    /// Deliberately not "it keeps its own title". Real Google, asked to patch a master's summary, overwrites
    /// the summary an exception was carrying — established by making that exact patch with the oracle
    /// credential and reading the result back before FamilyHQ could have touched it. The standard is what
    /// Google actually does, so a scenario demanding the override survive would be demanding that FamilyHQ
    /// diverge from the system of record. What the title ends up as is therefore left to the title-by-title
    /// comparison against Google's own expansion; what is asserted here is the structure, where the damage
    /// would be irreversible: an exception deleted takes the family's one deliberately different occurrence
    /// with it, and a second one means the series now expands two events into the same slot.
    /// </para>
    /// </summary>
    [Then(@"Google still holds that occurrence as one exception against its original slot")]
    public async Task ThenGoogleStillHoldsThatOccurrenceAsOneExceptionAgainstItsOriginalSlot()
    {
        var state = State;
        var exceptions = await SmokeSeries.ExceptionsAsync(state);

        var exception = exceptions.Should().ContainSingle(
            "the occurrence that had been singled out must still be exactly one exception after the "
            + "whole-series change. None at all means the series change absorbed it and the family's one "
            + "deliberately different occurrence is gone; two means the slot is now filled twice")
            .Subject;

        SmokeSeries.OriginalSlotOf(exception).Should().Be(
            _overrideStart!.Value,
            "the exception must still be pinned to the slot it replaces. An exception that lost its original "
            + "slot is expanded alongside the series' own occurrence for that day, and the family sees both");
    }

    [Then(@"the series keeps its original dates")]
    public async Task ThenTheSeriesKeepsItsOriginalDates()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Select(SmokeSeries.StartOf).Should().Equal(
            _originalStarts,
            "a change to every event in a series was a change of title. A series whose occurrences have "
            + "moved has been re-anchored by a write that was asked to rename it, which is the damage that "
            + "shows up months later and on the phone rather than here");
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renames occurrence <paramref name="index"/> of the scenario's series through the kiosk, at
    /// <paramref name="scope"/>, and returns the new title.
    /// <para>
    /// The occurrence is chosen from <b>Google's</b> expansion rather than from a date this suite worked
    /// out, so the day the kiosk is driven to is the day Google says that occurrence falls on.
    /// </para>
    /// </summary>
    private async Task<string> RenameOccurrenceAsync(int index, string baseTitle, SmokeRecurrenceScope scope)
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCountGreaterThan(
            index,
            $"the series must have an occurrence at position {index} for this scenario to edit one; Google "
            + $"expands it to {instances.Count}");

        await RememberSeriesBeforeTheEditAsync(state, instances, index);

        var occurrence = instances[index];
        var renamed = state.Correlation.Title(baseTitle);

        await state.RequireDashboard().RenameOccurrenceAsync(
            state.Correlation.ShortId, renamed, SmokeSeries.DateOf(occurrence), scope);

        state.ExpectedTitle = renamed;
        return renamed;
    }

    /// <summary>
    /// Records what the series looked like before the first edit — the master, its rule, its title and the
    /// full set of occurrence instants — so afterwards the assertions can say what changed rather than only
    /// what is there now.
    /// </summary>
    private async Task RememberSeriesBeforeTheEditAsync(
        SmokeScenarioState state, IReadOnlyList<GoogleEvent> instances, int index)
    {
        _splitStart = SmokeSeries.StartOf(instances[index]);

        if (_originalMasterId is not null)
        {
            return;
        }

        // Read from Google rather than from what the suite asked for: "unchanged" has to mean unchanged
        // from what the system of record actually held, not from what this scenario believes it wrote.
        var master = await SmokeSeries.MasterEventAsync(state);

        _originalMasterId = state.SeriesMasters.Single().GoogleEventId;
        _originalTitle = master.Summary;
        _originalRule = SmokeSeries.RequireSingleRule(master);
        _originalStarts.AddRange(instances.Select(SmokeSeries.StartOf));
    }

    /// <summary>
    /// Waits for Google to hold the expected number of exceptions, then returns them.
    /// <para>
    /// A bounded wait rather than a read, because the kiosk's write has only just been acknowledged and
    /// Google's own listing is what has to catch up. When it expires, the exception was never created.
    /// </para>
    /// </summary>
    private static async Task<IReadOnlyList<GoogleEvent>> ExceptionsSettledAsync(
        SmokeScenarioState state, int expected, string what)
    {
        return await BoundedWait.ForAsync<IReadOnlyList<GoogleEvent>>(
            async () =>
            {
                var exceptions = await SmokeSeries.ExceptionsAsync(state);
                return exceptions.Count == expected ? exceptions : null;
            },
            $"Google never settled on {expected} exception(s) for {what}. A change asked for at 'this "
            + "event' that produced none was applied to something wider than the one occurrence the user "
            + "chose",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));
    }
}
