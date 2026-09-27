using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RG2, RG3, RG8 and RG9 — series made, changed and removed in Google, as a phone would.
/// <para>
/// Each of these is a shape the kiosk has to read rather than write: a series bounded by a date instead of
/// a count, a yearly all-day series, a membership change made in a description, and a series removed
/// outright. The occurrence set is Google's, and the kiosk either matches it or does not.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceFromGoogleSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    // ── RG2: bounded by a date rather than a count ──────────────────────────────

    /// <summary>
    /// A series that ends on a date. <c>UNTIL</c> is inclusive and stated in UTC, so an implementation that
    /// treats it as exclusive, or that compares it in the family's zone, loses or gains the last occurrence —
    /// and only ever the last one, which is why nothing but the boundary shows the fault.
    /// </summary>
    [When(@"a weekly series bounded by an end date is created in Google")]
    public async Task WhenAWeeklySeriesBoundedByAnEndDateIsCreatedInGoogle()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);
        var firstDate = state.EventDay;

        // The end date lands exactly on the last wanted occurrence's start, which is the boundary an
        // inclusive UNTIL is supposed to keep and an exclusive reading would drop.
        var lastWanted = firstDate.AddDays(7 * (SmokeSeries.Occurrences - 1));
        var until = FamilyClock.ToFamilyOffset(lastWanted.ToDateTime(SmokeEventShape.StartTime));
        var rule = SmokeIcal.WeeklyOnUntil(firstDate.DayOfWeek, until);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Series ending on a date",
            "Weekly series bounded by an end date rather than a count",
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

    [Then(@"the last occurrence preprod serves is the last one Google gives")]
    public async Task ThenTheLastOccurrencePreprodServesIsTheLastOneGoogleGives()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCountGreaterThan(
            1,
            "a series bounded by an end date has to produce more than one occurrence for its boundary to mean "
            + "anything");

        var lastAccordingToGoogle = SmokeSeries.StartOf(instances[^1]);

        var served = await SmokeLookup.FindInPreprodAsync(
            state, SmokeLookup.MonthsCovering(instances.Select(SmokeSeries.DateOf)));

        served.Max(occurrence => occurrence.Start).Should().Be(
            lastAccordingToGoogle,
            "an end date is inclusive, and it is stated in UTC. Stopping a week early drops an occurrence the "
            + "family put in the calendar; running a week late invents one Google never expanded");
    }

    // ── RG3: a yearly all-day series ────────────────────────────────────────────

    [When(@"a yearly all-day series is created in Google")]
    public async Task WhenAYearlyAllDaySeriesIsCreatedInGoogle()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);
        var firstDate = state.EventDay;
        var rule = SmokeIcal.Yearly(SmokeSeries.YearlyOccurrences);

        var draft = SmokeEventShape.PhoneStyleAllDayDraft(
            state.Correlation,
            "Birthday from Google",
            "Yearly all-day series created directly in Google",
            firstDate,
            recurrence: [rule]);

        // A yearly series' second occurrence is a whole year out, so every lookup in this scenario has to
        // reach past it. Everything else in the suite is inside three weeks.
        state.WindowDaysAfter = SmokeLookup.YearlyWindowDaysAfter;

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.SeededRecurrenceRule = rule;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
        state.SeriesMasters = [new SmokeSeriesMaster(member, state.SeededGoogleEvent.Id)];
    }

    [Then(@"Google's expansion of it falls on the same date in consecutive years")]
    public async Task ThenGooglesExpansionOfItFallsOnTheSameDateInConsecutiveYears()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCount(
            SmokeSeries.YearlyOccurrences,
            "Google is the oracle for how many occurrences a bounded yearly rule produces");

        var dates = instances.Select(SmokeSeries.DateOf).ToList();

        dates.Select(date => (date.Month, date.Day)).Distinct().Should().HaveCount(
            1,
            "a yearly series falls on the same month and day every year. A second occurrence that has slipped "
            + "is what an occurrence derived by adding a fixed number of days looks like across a leap year");

        dates.Select(date => date.Year).Should().Equal(
            [dates[0].Year, dates[0].Year + 1],
            "two occurrences of a yearly rule are one year apart, in consecutive years");

        instances.Should().AllSatisfy(
            instance => instance.Start!.Date.Should().NotBeNull(
                "an all-day series expands to dates, not to times. A dateTime here means the all-day nature "
                + "was lost and every occurrence would be drawn in a time slot"));
    }

    // ── RG8: the members named on a series, changed on a phone ──────────────────

    [Given(@"a bounded weekly series naming two members exists in Google's shared calendar")]
    public async Task GivenABoundedWeeklySeriesNamingTwoMembersExistsInGooglesSharedCalendar()
    {
        var state = State;
        var members = state.Environment.Configuration.MemberCalendarNames;

        members.Should().HaveCountGreaterThanOrEqualTo(
            3, "Smoke__MemberCalendars must name at least three member calendars for one to be swapped");

        var shared = state.Environment.Configuration.SharedCalendar;
        var calendarId = state.Environment.Calendars.RequireGoogleId(shared);
        var firstDate = state.EventDay;
        var rule = SmokeIcal.WeeklyOn([firstDate.DayOfWeek], SmokeSeries.Occurrences);

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Shared series",
            MembersTag(members[0], members[1]),
            firstDate,
            recurrence: [rule]);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = shared;
        state.SeededRecurrenceRule = rule;
        state.MemberNames = [members[0], members[1]];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
        state.SeriesMasters = [new SmokeSeriesMaster(shared, state.SeededGoogleEvent.Id)];

        await SeriesSteps.WaitForPreprodToHoldEveryOccurrenceAsync(state, "the shared series");
    }

    /// <summary>
    /// Changes the members named on the master, keeping the count at two, so the change is a change of
    /// membership and not also a move between the shared container and a member calendar.
    /// </summary>
    [When(@"the series' members are changed in Google to a different pair")]
    public async Task WhenTheSeriesMembersAreChangedInGoogleToADifferentPair()
    {
        var state = State;
        var members = state.Environment.Configuration.MemberCalendarNames;
        var replacement = new[] { members[1], members[2] };
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        await state.Environment.Google.PatchEventAsync(
            calendarId,
            state.RequireSeededGoogleEvent().Id,
            new GoogleEventPatch(
                Description: state.Correlation.Description(MembersTag(replacement[0], replacement[1]))));

        state.MemberNames = replacement;
    }

    [Then(@"preprod serves every occurrence for the newly named members and no others")]
    public async Task ThenPreprodServesEveryOccurrenceForTheNewlyNamedMembersAndNoOthers()
    {
        var state = State;
        var expected = state.MemberNames.OrderBy(name => name, StringComparer.Ordinal).ToList();
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);
        var months = SmokeLookup.MonthsCovering(instances.Select(SmokeSeries.DateOf));

        await SmokeLookup.WaitForPreprodAsync(
            state,
            months,
            candidates => candidates.Count == instances.Count
                          && candidates.All(candidate => MemberNamesOf(candidate).SequenceEqual(expected)),
            $"preprod never settled on all {instances.Count} occurrences belonging to "
            + $"{string.Join(" and ", state.MemberNames)}. Membership belongs to the series, so a change made "
            + "on a phone has to reach every occurrence: one left behind is an occurrence a member still sees "
            + "that is no longer theirs");
    }

    // ── RG9: the series removed ─────────────────────────────────────────────────

    [When(@"that series is deleted in Google")]
    public async Task WhenThatSeriesIsDeletedInGoogle()
    {
        var state = State;
        var calendarId = state.Environment.Calendars.RequireGoogleId(state.SeededCalendarName!);

        await state.Environment.Google.DeleteEventAsync(calendarId, state.RequireSeededGoogleEvent().Id);
    }

    private static string MembersTag(string first, string second) =>
        $"Swimming\n[members: {first}, {second}]";

    private static IEnumerable<string> MemberNamesOf(PreprodEvent occurrence) =>
        occurrence.Members
            .Select(member => member.DisplayName)
            .OrderBy(name => name, StringComparer.Ordinal);
}
