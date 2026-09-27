using FamilyHQ.Smoke.Common.Pages;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RK2, RK3, RK4 and RK12 — the rule the kiosk actually writes, and what Google makes of it.
/// <para>
/// Each scenario here is about a part of the rule that FamilyHQ could drop without noticing: an interval,
/// an explicit weekday, a yearly anchor, the rule itself. None of them can be checked by asking FamilyHQ
/// what it sent, because the question is what Google received and what it expands that to.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceShapeSteps(ScenarioContext scenarioContext)
{
    private const int FortnightlyInterval = 2;

    private static readonly DayOfWeek ChosenWeekday = DayOfWeek.Wednesday;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    // ── RK2: an interval and a chosen weekday ───────────────────────────────────

    /// <summary>
    /// A series on a weekday that is <b>not</b> the start date's, repeating every second week.
    /// <para>
    /// Both halves are deliberate. A rule that quietly re-derives its weekday from the start date looks
    /// right whenever the two agree, and an interval that is dropped produces a series that is simply
    /// weekly — which also looks plausible until the family finds an event on a week they had free.
    /// </para>
    /// </summary>
    [When(@"I create a bounded fortnightly series on a chosen weekday on the kiosk")]
    public async Task WhenICreateABoundedFortnightlySeriesOnAChosenWeekdayOnTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var date = SmokeEventShape.NextDate(state.EventDay, ChosenWeekday);

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Fortnightly rehearsal"),
            Description: state.Correlation.Description("Every second week, on one chosen weekday"),
            CalendarNames: [member],
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: SmokeRecurrence.EveryNWeeks(
                FortnightlyInterval, [ChosenWeekday], SmokeSeries.Occurrences));

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google's rule for it repeats every 2 weeks on that weekday")]
    public async Task ThenGooglesRuleForItRepeatsEvery2WeeksOnThatWeekday()
    {
        var state = State;
        var master = await RecordTheOnlyMasterAsync(state, "the fortnightly series");
        var rule = SmokeSeries.RequireSingleRule(master.Event);

        rule.Should().Contain("FREQ=WEEKLY", "the kiosk was asked for a weekly series");
        rule.Should().Contain(
            $"INTERVAL={FortnightlyInterval}",
            "an interval that never reaches Google turns a fortnightly series into a weekly one, and the "
            + "family finds an event on a week they had free");
        rule.Should().Contain(
            $"BYDAY={SmokeIcal.Day(ChosenWeekday)}",
            "the weekday the user chose must be stated in the rule. A rule that leaves it out lets Google "
            + "derive it from the start date, which agrees with the choice right up until the two differ");
        rule.Should().Contain(
            $"COUNT={SmokeSeries.Occurrences}", "the series must be bounded by the count the kiosk was given");
    }

    // ── RK3: a yearly all-day series ────────────────────────────────────────────

    /// <summary>
    /// The birthday shape: all-day, once a year. The two things it combines are exactly the two that make
    /// the anchor fragile — an all-day event carries no zone, and the next occurrence is a year away, far
    /// enough for a daylight-saving change to sit between them.
    /// </summary>
    [When(@"I create a bounded yearly all-day series on the kiosk")]
    public async Task WhenICreateABoundedYearlyAllDaySeriesOnTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var date = state.EventDay;

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Birthday"),
            Description: state.Correlation.Description("Yearly all-day series created by the smoke suite"),
            CalendarNames: [member],
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: SmokeRecurrence.Yearly(SmokeSeries.YearlyOccurrences),
            IsAllDay: true);

        // A yearly series' second occurrence is a whole year out, so the window every lookup in this
        // scenario uses has to reach past it. Everything else in the suite is inside three weeks.
        state.WindowDaysAfter = SmokeLookup.YearlyWindowDaysAfter;

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google's rule for it repeats yearly and its occurrences are dates rather than times")]
    public async Task ThenGooglesRuleForItRepeatsYearlyAndItsOccurrencesAreDatesRatherThanTimes()
    {
        var state = State;
        var master = await RecordTheOnlyMasterAsync(state, "the yearly all-day series");
        var rule = SmokeSeries.RequireSingleRule(master.Event);

        rule.Should().Contain("FREQ=YEARLY", "the kiosk was asked for a yearly series");
        rule.Should().Contain(
            $"COUNT={SmokeSeries.YearlyOccurrences}",
            "a yearly series left unbounded would keep adding an occurrence to a live calendar every year "
            + "for ever, and the smoke events are deliberately never cleaned up");

        master.Event.Start!.Date.Should().NotBeNull(
            "an all-day series is a series of dates. A dateTime here means the all-day flag was lost on the "
            + "way out, and every occurrence would be drawn in a time slot instead of at the top of the day");

        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        instances.Should().HaveCount(
            SmokeSeries.YearlyOccurrences, "Google is the oracle for how many occurrences a bounded rule has");

        instances.Select(instance => SmokeSeries.DateOf(instance).Day)
            .Distinct()
            .Should().HaveCount(
                1,
                "a yearly series falls on the same day of the month every year. A second occurrence that has "
                + "slipped a day is what an occurrence derived by adding 365 days looks like in a leap year");

        instances.Select(instance => SmokeSeries.DateOf(instance).Year)
            .Should().OnlyHaveUniqueItems("a yearly series has one occurrence per year, not two in one");
    }

    // ── RK4: a two-member series ────────────────────────────────────────────────

    [When(@"I create a bounded weekly series on the kiosk for two members")]
    public async Task WhenICreateABoundedWeeklySeriesOnTheKioskForTwoMembers()
    {
        var state = State;
        var members = state.Environment.Configuration.MemberCalendarNames.Take(2).ToList();
        members.Should().HaveCount(2, "Smoke__MemberCalendars must name at least two member calendars");

        var date = state.EventDay;

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Two-member series"),
            Description: state.Correlation.Description("Bounded weekly series for two members"),
            CalendarNames: members,
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: SmokeRecurrence.Weekly([date.DayOfWeek], SmokeSeries.Occurrences));

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = members;
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google holds one series master on the shared calendar naming both members")]
    public async Task ThenGoogleHoldsOneSeriesMasterOnTheSharedCalendarNamingBothMembers()
    {
        var state = State;
        var shared = state.Environment.Configuration.SharedCalendar;

        var master = await RecordTheOnlyMasterAsync(state, "the two-member series");

        master.CalendarName.Should().Be(
            shared,
            "a multi-member series is written once, to the shared container. A copy on each member's own "
            + "calendar is two series the family then has to edit twice and sees twice");

        SmokeMemberTag.NamesIn(master.Event.Description).Should().BeEquivalentTo(
            state.MemberNames,
            "the members tag is how the Google side records who a shared-calendar series belongs to, and it "
            + "is written on the master so every occurrence inherits it");
    }

    [Then(@"preprod serves every occurrence for both members")]
    public async Task ThenPreprodServesEveryOccurrenceForBothMembers()
    {
        var state = State;
        var instances = await SmokeSeries.InstancesForScenarioAsync(state);

        var served = await SmokeSeries.AssertPreprodServesGooglesInstancesAsync(
            state, instances, "the two-member series");

        foreach (var occurrence in served)
        {
            occurrence.Members.Select(member => member.DisplayName).Should().BeEquivalentTo(
                state.MemberNames,
                "membership belongs to the series, so every occurrence must carry it. An occurrence that "
                + "belongs to only one of the two is one the other member never sees");
        }
    }

    // ── RK12: the repeat switched off ───────────────────────────────────────────

    [When(@"I switch the repeat off on the kiosk")]
    public async Task WhenISwitchTheRepeatOffOnTheKiosk()
    {
        var state = State;
        await state.RequireDashboard().TurnOffRecurrenceAsync(state.Correlation.ShortId, state.EventDate);
    }

    [Then(@"Google holds the event with no recurrence rule")]
    public async Task ThenGoogleHoldsTheEventWithNoRecurrenceRule()
    {
        var state = State;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1 && !SmokeSeries.IsRecurrenceMaster(candidates[0].Event),
            "Google never settled on a single event with no recurrence rule after the repeat was switched "
            + "off. A rule left behind means the series is still expanding on the family's calendar, and a "
            + "second event means the collapse created one instead of clearing one");

        found.Single().Event.Start!.DateTime.Should().Be(
            SmokeEventShape.GoogleBoundary(state.EventDate, SmokeEventShape.StartTime).DateTime,
            "collapsing a series leaves the event it started from where it was. Clearing the rule must not "
            + "also move the event that survives");
    }

    [Then(@"preprod serves exactly one occurrence of it")]
    public async Task ThenPreprodServesExactlyOneOccurrenceOfIt()
    {
        var state = State;

        await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1,
            "preprod never settled on exactly one occurrence after the repeat was switched off. Occurrences "
            + "the family can still see on the kiosk after collapsing a series are occurrences Google no "
            + "longer has");
    }

    private static async Task<SmokeGoogleLocation> RecordTheOnlyMasterAsync(
        SmokeScenarioState state, string what)
    {
        var masters = await SmokeSeries.RecordMastersAsync(state, 1, what);
        return masters.Single();
    }
}
