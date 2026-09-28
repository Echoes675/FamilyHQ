using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RG4, RG5, RG6 and RG7 — the three kinds of series edit a phone makes, performed directly in Google.
/// <para>
/// Performed as the app performs them rather than through any FamilyHQ verb, because the question is whether
/// the kiosk can <i>read</i> what the app leaves behind. The shapes are genuinely different data: an
/// exception carrying the slot it replaces, a truncated series beside a second one, and a patched master with
/// an exception still hanging off it.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceEditedInGoogleSteps(ScenarioContext scenarioContext)
{
    private const int MiddleOccurrence = 1;

    /// <summary>
    /// How far the moved occurrence is shifted. Two hours: enough that the kiosk cannot show the old time by
    /// accident, and small enough to stay on the same day, so "only this occurrence changed" is a statement
    /// about a time rather than about a date as well.
    /// </summary>
    private static readonly TimeSpan MoveBy = TimeSpan.FromHours(2);

    private DateTimeOffset? _splitStart;
    private DateTimeOffset? _deletedStart;
    private string? _overrideTitle;
    private string? _originalMasterId;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    // ── RG4: one occurrence moved and renamed ───────────────────────────────────

    [When(@"the middle occurrence is moved and renamed in Google")]
    public async Task WhenTheMiddleOccurrenceIsMovedAndRenamedInGoogle()
    {
        var state = State;
        var occurrence = await OccurrenceAsync(state, MiddleOccurrence);
        var newStart = SmokeSeries.StartOf(occurrence) + MoveBy;
        var newEnd = SmokeSeries.EndOf(occurrence) + MoveBy;

        _splitStart = SmokeSeries.StartOf(occurrence);
        _overrideTitle = state.Correlation.Title("Occurrence moved on the phone");

        await PatchAsync(
            state,
            occurrence.Id,
            new GoogleEventPatch(
                Summary: _overrideTitle,
                Start: ZonedBoundary(newStart),
                End: ZonedBoundary(newEnd)));
    }

    [Then(@"Google holds that occurrence alone as an exception at its new time")]
    public async Task ThenGoogleHoldsThatOccurrenceAloneAsAnExceptionAtItsNewTime()
    {
        var state = State;
        var exceptions = await SmokeSeries.ExceptionsAsync(state);

        var exception = exceptions.Should().ContainSingle(
            "editing one occurrence in the Google Calendar app produces exactly one exception. Without one "
            + "this scenario has not set up the situation it is about, and what follows would prove nothing")
            .Subject;

        exception.Summary.Should().Be(
            _overrideTitle, "the exception is the occurrence that was renamed");

        SmokeSeries.OriginalSlotOf(exception).Should().Be(
            _splitStart!.Value, "an exception records the slot of the series it replaces");

        SmokeSeries.StartOf(exception).Should().Be(
            _splitStart!.Value + MoveBy, "the occurrence was moved, so Google holds it at its new time");
    }

    // ── RG5: one occurrence deleted ─────────────────────────────────────────────

    [When(@"the middle occurrence is deleted in Google")]
    public async Task WhenTheMiddleOccurrenceIsDeletedInGoogle()
    {
        var state = State;
        var occurrence = await OccurrenceAsync(state, MiddleOccurrence);

        _deletedStart = SmokeSeries.StartOf(occurrence);

        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);
        await state.Environment.Google.DeleteEventAsync(calendarId, occurrence.Id);
    }

    [Then(@"preprod no longer serves the occurrence that was deleted")]
    public async Task ThenPreprodNoLongerServesTheOccurrenceThatWasDeleted()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Select(SmokeSeries.StartOf).Should().NotContain(
            _deletedStart!.Value,
            "Google has stopped expanding the cancelled slot, so this scenario is asking the right question "
            + "of preprod — if Google still expanded it, the delete never landed");

        await SmokeLookup.WaitForPreprodAsync(
            state,
            SmokeLookup.MonthsCovering(instances.Select(SmokeSeries.DateOf)),
            candidates => candidates.All(candidate => candidate.Start != _deletedStart!.Value),
            "preprod is still serving the occurrence that was deleted in Google. A single occurrence the "
            + "family removed on a phone that stays on the kiosk is the visible half of a broken inbound sync, "
            + "and it is the shape most easily mistaken for the series being intact");
    }

    // ── RG6: the series split, the way the phone app splits one ─────────────────

    /// <summary>
    /// A "this and following" change as the Google Calendar app performs it: the original series is truncated
    /// to end just before the chosen occurrence, and a second series is inserted carrying the remainder with
    /// the new details. Two series, one calendar, and the family sees the join.
    /// </summary>
    [When(@"the series is split in Google at its middle occurrence")]
    public async Task WhenTheSeriesIsSplitInGoogleAtItsMiddleOccurrence()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);
        var occurrence = instances[MiddleOccurrence];

        _splitStart = SmokeSeries.StartOf(occurrence);
        _originalMasterId = state.SeriesMasters.Single().GoogleEventId;

        var calendarName = state.SeededCalendarName!;
        var calendarId = state.Environment.Calendars.RequireGoogleId(calendarName);
        var remaining = instances.Count - MiddleOccurrence;

        await state.Environment.Google.PatchEventAsync(
            calendarId,
            _originalMasterId,
            new GoogleEventPatch(
                Recurrence: [SmokeIcal.TruncatedBefore(state.SeededRecurrenceRule!, _splitStart.Value)]));

        var splitDate = SmokeSeries.DateOf(occurrence);
        var continuation = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Series from the phone split",
            "The second half a phone-made split leaves behind",
            splitDate,
            recurrence: [SmokeIcal.WeeklyOn([splitDate.DayOfWeek], remaining)]);

        var inserted = await state.Environment.Google.InsertEventAsync(calendarId, continuation);

        state.SeriesMasters =
        [
            new SmokeSeriesMaster(calendarName, _originalMasterId),
            new SmokeSeriesMaster(calendarName, inserted.Id)
        ];
    }

    [Then(@"Google holds the original series ending before the split and a second series from it")]
    public async Task ThenGoogleHoldsTheOriginalSeriesEndingBeforeTheSplitAndASecondSeriesFromIt()
    {
        var state = State;
        var masters = await SmokeSeries.WaitForMastersAsync(
            state, 2, "the two halves a phone-made split leaves behind");

        var original = masters.Single(candidate => candidate.Event.Id == _originalMasterId);
        var continuation = masters.Single(candidate => candidate.Event.Id != _originalMasterId);

        SmokeSeries.RequireSingleRule(original.Event).Should().Contain(
            "UNTIL=",
            "the truncation is what makes this a split rather than two overlapping series. Without it the "
            + "kiosk would be asked to show every later occurrence twice — which is a real thing the family "
            + "can do to themselves on a phone, but not what this scenario set up");

        SmokeSeries.StartOf(continuation.Event).Should().Be(
            _splitStart!.Value, "the second series begins at the occurrence the split was made on");
    }

    // ── RG7: the master renamed over an existing override ───────────────────────

    [Given(@"the first occurrence has been given its own title in Google")]
    public async Task GivenTheFirstOccurrenceHasBeenGivenItsOwnTitleInGoogle()
    {
        var state = State;
        var occurrence = await OccurrenceAsync(state, 0);

        _overrideTitle = state.Correlation.Title("Occurrence with its own title");
        _splitStart = SmokeSeries.StartOf(occurrence);

        await PatchAsync(state, occurrence.Id, new GoogleEventPatch(Summary: _overrideTitle));

        var exceptions = await SmokeSeries.ExceptionsAsync(state);

        exceptions.Should().ContainSingle(
            "this scenario is about what a whole-series rename does to an occurrence that had already been "
            + "singled out, so singling one out has to have worked first. A failure here is the precondition, "
            + "not the behaviour under test")
            .Which.Summary.Should().Be(
                _overrideTitle, "the exception must carry the override title before the series is renamed");
    }

    [When(@"the series title is changed on the master in Google")]
    public async Task WhenTheSeriesTitleIsChangedOnTheMasterInGoogle()
    {
        var state = State;
        var renamed = state.Correlation.Title("Series renamed on the phone");

        await PatchAsync(state, state.SeriesMasters.Single().GoogleEventId, new GoogleEventPatch(Summary: renamed));

        state.ExpectedTitle = renamed;
    }

    /// <summary>
    /// What the master rename must leave of the singled-out occurrence: one exception, still pinned to the
    /// slot it replaces.
    /// <para>
    /// Deliberately <b>not</b> "it keeps its own title". Asked to patch a master's summary, real Google
    /// overwrites the summary an exception was carrying — established here, with the oracle credential, by
    /// making that exact patch and reading the result back before FamilyHQ could have touched anything. So
    /// what the title becomes is Google's business and is left to the title-by-title comparison; what is
    /// asserted here is the structure, where the damage would be permanent.
    /// </para>
    /// </summary>
    [Then(@"that occurrence is still a single exception in Google")]
    public async Task ThenThatOccurrenceIsStillASingleExceptionInGoogle()
    {
        var state = State;

        await BoundedWait.UntilAsync(
            async () => (await SmokeSeries.MasterEventAsync(state)).Summary == state.ExpectedTitle,
            "Google never took the rename on the master, so there is no whole-series change for the kiosk to "
            + "have followed and the scenario would pass on a write that never happened",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));

        var exception = (await SmokeSeries.ExceptionsAsync(state)).Should().ContainSingle(
            "the singled-out occurrence must still be exactly one exception after the master was renamed. "
            + "None means the rename absorbed it; two means the slot is now filled twice")
            .Subject;

        SmokeSeries.OriginalSlotOf(exception).Should().Be(
            _splitStart!.Value,
            "the exception must still be pinned to the slot it replaces, or the series expands its own "
            + "occurrence for that day alongside it and the family sees both");
    }

    // ── Shared ──────────────────────────────────────────────────────────────────

    private async Task<GoogleEvent> OccurrenceAsync(SmokeScenarioState state, int index)
    {
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCountGreaterThan(
            index,
            $"the series must have an occurrence at position {index}; Google expands it to {instances.Count}");

        return instances[index];
    }

    private static Task<GoogleEvent> PatchAsync(
        SmokeScenarioState state, string eventId, GoogleEventPatch patch) =>
        state.Environment.Google.PatchEventAsync(
            state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!), eventId, patch);

    /// <summary>
    /// A boundary for an occurrence that is being moved: the instant, plus the family's zone, which is the
    /// zone the series is already anchored to. Sending both is what the phone app does, and it is what keeps
    /// the occurrence anchored rather than merely dated.
    /// </summary>
    private static GoogleEventDateTime ZonedBoundary(DateTimeOffset instant) =>
        new(instant, Date: null, TimeZone: FamilyClock.TimeZoneId);
}
