using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RK8, RK9 and RK10 — deleting from a series at each of the three scopes.
/// <para>
/// What survives is read from Google. A delete that succeeded against FamilyHQ and not against Google is
/// not a delete at all: the next sync brings the event back, and asking FamilyHQ whether it deleted
/// something would have said yes both times.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceDeleteScopeSteps(ScenarioContext scenarioContext)
{
    private const int MiddleOccurrence = 1;

    private readonly List<DateTimeOffset> _startsBeforeTheDelete = [];

    private DateTimeOffset? _deletedStart;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    [When(@"I delete the middle occurrence on the kiosk for this event only")]
    public Task WhenIDeleteTheMiddleOccurrenceForThisEventOnly() =>
        DeleteOccurrenceAsync(SmokeRecurrenceScope.ThisEvent);

    [When(@"I delete the middle occurrence on the kiosk for this and following events")]
    public Task WhenIDeleteTheMiddleOccurrenceForThisAndFollowing() =>
        DeleteOccurrenceAsync(SmokeRecurrenceScope.ThisAndFollowing);

    [When(@"I delete the middle occurrence on the kiosk for all events")]
    public Task WhenIDeleteTheMiddleOccurrenceForAllEvents() =>
        DeleteOccurrenceAsync(SmokeRecurrenceScope.AllEvents);

    // ── RK8: this event only ────────────────────────────────────────────────────

    [Then(@"Google's expansion no longer includes that occurrence but still includes the others")]
    public async Task ThenGooglesExpansionNoLongerIncludesThatOccurrenceButStillIncludesTheOthers()
    {
        var state = State;
        var expected = _startsBeforeTheDelete.Where(start => start != _deletedStart!.Value).ToList();

        var remaining = await SettledInstancesAsync(
            state,
            expected.Count,
            "the occurrences left after one was cancelled. A delete asked for at 'this event' that removed "
            + "more than one occurrence took days the family never offered up");

        remaining.Select(SmokeSeries.StartOf).Should().Equal(
            expected,
            "cancelling one occurrence leaves every other occurrence exactly where it was. Google stops "
            + "expanding the cancelled slot and expands the rest unchanged");
    }

    // ── RK9: this and following ─────────────────────────────────────────────────

    [Then(@"Google holds the series ending before that occurrence")]
    public async Task ThenGoogleHoldsTheSeriesEndingBeforeThatOccurrence()
    {
        var state = State;
        var expected = _startsBeforeTheDelete.Where(start => start < _deletedStart!.Value).ToList();

        expected.Should().NotBeEmpty(
            "the occurrence deleted from must not be the first one, or 'this and following' would be "
            + "indistinguishable from deleting the whole series and the scenario would prove nothing");

        var masters = await SmokeSeries.RecordMastersAsync(
            state, 1, "the series left behind after a 'this and following' delete");

        SmokeSeries.RequireSingleRule(masters.Single().Event).Should().Contain(
            "UNTIL=",
            "the series has to end just before the occurrence the delete started at. A rule left as it was "
            + "means nothing was actually removed on Google's side, and the occurrences come back on the "
            + "next sync");

        var remaining = await SettledInstancesAsync(
            state, expected.Count, "the occurrences before the one the delete started at");

        remaining.Select(SmokeSeries.StartOf).Should().Equal(
            expected,
            "everything before the split is the family's history and must be untouched; everything from it "
            + "onwards is what they asked to remove");
    }

    // ── RK10: all events ────────────────────────────────────────────────────────

    [Then(@"Google holds nothing at all for this scenario")]
    public async Task ThenGoogleHoldsNothingAtAllForThisScenario()
    {
        var state = State;

        await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 0,
            "Google still lists something carrying this scenario's correlation marker after the whole series "
            + "was deleted from the kiosk. A master left behind keeps expanding, and the family sees the "
            + "series they deleted reappear on the next sync");
    }

    [Then(@"preprod serves nothing for this scenario")]
    public async Task ThenPreprodServesNothingForThisScenario()
    {
        var state = State;

        await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 0,
            "preprod is still serving occurrences of a series that has been deleted from Google");
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes the middle occurrence at <paramref name="scope"/>, remembering first what Google expanded the
    /// series to — so afterwards the assertions can name which occurrences went and which stayed.
    /// </summary>
    private async Task DeleteOccurrenceAsync(SmokeRecurrenceScope scope)
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCountGreaterThan(
            MiddleOccurrence,
            "the series must have a middle occurrence for this scenario to delete one; Google expands it to "
            + $"{instances.Count}");

        _startsBeforeTheDelete.AddRange(instances.Select(SmokeSeries.StartOf));
        _deletedStart = _startsBeforeTheDelete[MiddleOccurrence];

        await state.RequireDashboard().DeleteOccurrenceAsync(
            state.Correlation.ShortId, SmokeSeries.DateOf(instances[MiddleOccurrence]), scope);
    }

    /// <summary>
    /// Waits for Google's expansion to settle on <paramref name="expectedCount"/> occurrences, then returns
    /// it. Google's listing is what has to catch up with the write the kiosk has just had acknowledged.
    /// </summary>
    private static Task<IReadOnlyList<GoogleEvent>> SettledInstancesAsync(
        SmokeScenarioState state, int expectedCount, string what) =>
        BoundedWait.ForAsync<IReadOnlyList<GoogleEvent>>(
            async () =>
            {
                var instances = await SmokeSeries.InstancesForScenarioAsync(state);
                return instances.Count == expectedCount ? instances : null;
            },
            $"Google never settled on {expectedCount} occurrence(s) for {what}",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));
}
