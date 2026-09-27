using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Common.Pages;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RD1 and RD2 — a bounded series either side of the next daylight-saving change, in both directions.
/// <para>
/// <b>Why the series is daily.</b> Three consecutive days is the shortest bounded series that can be placed
/// around a transition: the day before it, the day of it and the day after. A weekly series would have to
/// start a week earlier, which is not always in the future, and would put the whole scenario further out for
/// no extra coverage.
/// </para>
/// <para>
/// <b>Why the transition is discovered at run time.</b> A date written into the source would stop being in
/// the future, and the scenario would degrade into an ordinary recurrence check that still passes — worse
/// than having no scenario, because it would look like coverage. The date is therefore found by asking the
/// family's own zone when it next changes offset, and the assertion below <i>requires</i> the occurrences to
/// sit at two different offsets. If the placement ever stops spanning a change, the scenario says so.
/// </para>
/// </summary>
[Binding]
public sealed class DaylightSavingSteps(ScenarioContext scenarioContext)
{
    /// <summary>
    /// Three: the day before the change, the day of it and the day after. Two would do, but a middle
    /// occurrence makes the direction of a wrong answer obvious in the failure.
    /// </summary>
    private const int SpanningOccurrences = 3;

    private DateOnly _changeDate;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    [When(@"I create a bounded daily series spanning the next daylight-saving change on the kiosk")]
    public async Task WhenICreateABoundedDailySeriesSpanningTheNextChangeOnTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var firstDate = PlaceSeriesAroundTheChange(state);

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Across the clock change"),
            Description: state.Correlation.Description(
                "Bounded daily series placed either side of the next daylight-saving change"),
            CalendarNames: [member],
            Date: firstDate,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: SmokeRecurrence.Daily(SpanningOccurrences));

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Title;

        await SmokeSeries.RecordMastersAsync(
            state, 1, "the series placed either side of the next daylight-saving change");
    }

    [When(@"a bounded daily series spanning the next daylight-saving change is created in Google")]
    public async Task WhenABoundedDailySeriesSpanningTheNextChangeIsCreatedInGoogle()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);
        var firstDate = PlaceSeriesAroundTheChange(state);
        var rule = SmokeIcal.Daily(SpanningOccurrences);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Across the clock change, from Google",
            "Bounded daily series created directly in Google either side of the next clock change",
            firstDate,
            recurrence: [rule]);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.SeededRecurrenceRule = rule;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
        state.SeriesMasters = [new SmokeSeriesMaster(member, state.SeededGoogleEvent.Id)];
    }

    [Then(@"Google's occurrences for it span the change at an unchanged wall-clock time")]
    public async Task ThenGooglesOccurrencesForItSpanTheChangeAtAnUnchangedWallClockTime()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCount(
            SpanningOccurrences, "Google is the oracle for how many occurrences a bounded rule produces");

        var starts = instances.Select(SmokeSeries.StartOf).ToList();

        // The guard that keeps this scenario honest. A daily series that does NOT span a transition has
        // twenty-four hours between every pair of consecutive occurrences; one that does has a
        // twenty-three- or twenty-five-hour gap across it, because the wall clock stayed put and the
        // instant moved. If the placement ever stopped spanning a change — the zone's rules changed, the
        // arithmetic drifted — every other assertion here would still pass while testing nothing at all.
        //
        // The gaps, not the offsets on Google's own values: Google reports an instance's dateTime in the
        // calendar's default zone, so the offset it prints says something about the calendar rather than
        // about where the occurrence falls. The instants are the part that cannot be reformatted.
        var gaps = starts.Zip(starts.Skip(1), (earlier, later) => later - earlier).ToList();

        gaps.Should().Contain(
            gap => gap != TimeSpan.FromDays(1),
            $"this series was placed around {_changeDate:yyyy-MM-dd}, the next date the family's zone changes "
            + "its UTC offset, so consecutive occurrences cannot all be exactly twenty-four hours apart. All "
            + "at twenty-four hours means they no longer span the change, and nothing below would be testing "
            + "what this scenario exists for");

        starts.Select(start => FamilyClock.ToFamilyWallClock(start).TimeOfDay)
            .Distinct()
            .Should().Equal(
                [SmokeEventShape.StartTime.ToTimeSpan()],
                "a series is anchored to a wall-clock time in a zone: the family put it in at "
                + $"{SmokeEventShape.StartTime:HH\\:mm} and it stays at {SmokeEventShape.StartTime:HH\\:mm} "
                + "after the clocks change, which is why the instant moves by an hour and the displayed time "
                + "does not. An occurrence an hour out is a series stepped forward in fixed units instead of "
                + "in its own zone");

        starts.Select(start => FamilyClock.ToFamilyWallClock(start).Date)
            .Should().Equal(
                [
                    _changeDate.AddDays(-1).ToDateTime(TimeOnly.MinValue),
                    _changeDate.ToDateTime(TimeOnly.MinValue),
                    _changeDate.AddDays(1).ToDateTime(TimeOnly.MinValue)
                ],
                "the three occurrences are the day before the change, the day of it and the day after. A set "
                + "of dates other than those means a daily rule skipped or repeated a day across the "
                + "transition");
    }

    /// <summary>
    /// Works out where the series has to start for its three occurrences to be the day before the next
    /// daylight-saving change, the day of it, and the day after — and remembers the change date so the
    /// assertions can name it.
    /// </summary>
    private DateOnly PlaceSeriesAroundTheChange(SmokeScenarioState state)
    {
        _changeDate = FamilyClock.NextOffsetChangeDate();

        var firstDate = _changeDate.AddDays(-1);

        firstDate.Should().BeAfter(
            FamilyClock.Today,
            "a smoke scenario never creates an event in the past, and an occurrence behind the kiosk's own "
            + "clock could not be opened on the day view either");

        FamilyClock.OffsetOn(firstDate, SmokeEventShape.StartTime).Should().NotBe(
            FamilyClock.OffsetOn(_changeDate.AddDays(1), SmokeEventShape.StartTime),
            $"the day before {_changeDate:yyyy-MM-dd} and the day after it must sit at different UTC offsets "
            + "for this series to span the change at all. If they do not, the date found is not a "
            + "daylight-saving transition and the scenario has nothing to prove");

        state.EventDate = firstDate;
        return firstDate;
    }
}
