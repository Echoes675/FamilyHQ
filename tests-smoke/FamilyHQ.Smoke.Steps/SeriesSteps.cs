using FamilyHQ.Smoke.Common.Pages;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The series every recurrence-editing scenario starts from, and the comparison every one of them ends in.
/// <para>
/// Shared rather than repeated because the thing under test in those scenarios is the <i>edit</i>, not the
/// series: each of them needs a plain bounded series to exist first, on one side or the other, and each of
/// them finishes by asking whether preprod serves exactly what Google expands the rule to. A per-feature
/// copy of either would let one feature's copy drift and quietly stop proving the same thing.
/// </para>
/// </summary>
[Binding]
public sealed class SeriesSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    /// <summary>
    /// A bounded weekly series made on the kiosk, with its master located in Google before anything else
    /// happens — so a scenario that goes on to edit one occurrence is editing a series that demonstrably
    /// reached the system of record.
    /// </summary>
    [Given(@"the kiosk has created a bounded weekly series")]
    public async Task GivenTheKioskHasCreatedABoundedWeeklySeries()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var date = state.ReserveFirstDay(SmokeScenarioDays.Weekly(SmokeSeries.Occurrences));

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Series from the kiosk"),
            Description: state.Correlation.Description("Bounded weekly series created by the smoke suite"),
            CalendarNames: [member],
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: SmokeRecurrence.Weekly([date.DayOfWeek], SmokeSeries.Occurrences));

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;

        await SmokeSeries.RecordMastersAsync(state, 1, "the series the kiosk was asked to create");
    }

    /// <summary>
    /// A bounded weekly series made directly in Google, the way the Google Calendar app makes one — free
    /// text, a location, a colour and a reminder — and then waited for on the kiosk.
    /// <para>
    /// The waiting is part of the precondition, not a convenience: a scenario that edits a series in Google
    /// before the kiosk has ever seen it would prove nothing about how the kiosk follows an edit.
    /// </para>
    /// </summary>
    [Given(@"a bounded weekly series created in Google has reached the kiosk")]
    public async Task GivenABoundedWeeklySeriesCreatedInGoogleHasReachedTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);
        var firstDate = state.ReserveFirstDay(SmokeScenarioDays.Weekly(SmokeSeries.Occurrences));
        var rule = SmokeIcal.WeeklyOn([firstDate.DayOfWeek], SmokeSeries.Occurrences);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Series from Google",
            "Bounded weekly series created directly in Google",
            firstDate,
            recurrence: [rule]);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.SeededRecurrenceRule = rule;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
        state.SeriesMasters = [new SmokeSeriesMaster(member, state.SeededGoogleEvent.Id)];

        await WaitForPreprodToHoldEveryOccurrenceAsync(state, "the series created in Google");
    }

    /// <summary>The comparison that every recurrence scenario in this suite comes down to.</summary>
    [Then(@"the occurrences preprod serves match Google's expansion")]
    public async Task ThenTheOccurrencesPreprodServesMatchGooglesExpansion()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        await SmokeSeries.AssertPreprodServesGooglesInstancesAsync(state, instances, "this series");
    }

    /// <summary>
    /// The same comparison, extended to the title on each occurrence — for the scenarios where the point is
    /// that some occurrences changed and others did not.
    /// </summary>
    [Then(@"the occurrences preprod serves match Google's expansion, title by title")]
    public async Task ThenTheOccurrencesPreprodServesMatchGooglesExpansionTitleByTitle()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        await SmokeSeries.AssertPreprodAgreesWithGoogleTitleByTitleAsync(state, instances, "this series");
    }

    /// <summary>
    /// Waits for the live push to have delivered the whole series, so a later assertion about what changed
    /// is not racing the arrival of what was there to begin with.
    /// </summary>
    internal static async Task WaitForPreprodToHoldEveryOccurrenceAsync(
        SmokeScenarioState state, string what)
    {
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCount(
            SmokeSeries.Occurrences,
            $"Google must expand {what} to the occurrences the rule asks for before the kiosk can be "
            + "expected to show them");

        await SmokeLookup.WaitForPreprodAsync(
            state,
            SmokeLookup.MonthsCovering(instances.Select(SmokeSeries.DateOf)),
            candidates => candidates.Count == instances.Count,
            $"preprod never received all {instances.Count} occurrences of {what}. The series had to travel "
            + "Google → RelayRobin → preprod's webhook → the sync queue → the API, and one of those links "
            + "did not carry it");
    }
}
