using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RK11 — the golden rule, applied to a series.
/// <para>
/// The series is created <b>in Google</b>, anchored to a zone that is not the family's, because that is the
/// case FamilyHQ is most likely to get wrong: its own configured zone is close to hand, and using it as a
/// replacement for the zone Google supplied re-anchors every future occurrence. The kiosk then changes the
/// title and nothing else, so whatever Google holds afterwards for the other fields is entirely FamilyHQ's
/// choice of what to send back.
/// </para>
/// <para>
/// The failure this catches is invisible today. A re-anchored series still shows the right time until the
/// two zones' daylight-saving schedules diverge, at which point every future occurrence moves — on the
/// phone, months from now, with nothing to connect it to the edit that caused it.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceGoldenRuleSteps(ScenarioContext scenarioContext)
{
    /// <summary>
    /// The zone the phone-made series is anchored to. Chosen because it is neither the family's zone nor on
    /// the same daylight-saving schedule as it, so a substitution shows up as a different zone id rather
    /// than as a coincidentally equal offset.
    /// </summary>
    private const string ForeignTimeZoneId = "America/New_York";

    /// <summary>
    /// The wall-clock time the series runs at <b>in its own zone</b>. Mid-morning there, which is afternoon
    /// in the family's zone — still the same calendar day, so the kiosk's day view finds the occurrence on
    /// the day Google says it falls on.
    /// </summary>
    private static readonly TimeOnly ForeignStartTime = new(10, 0);

    private static readonly TimeOnly ForeignEndTime = new(11, 0);

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    [Given(@"a bounded weekly series created in Google in another time zone has reached the kiosk")]
    public async Task GivenABoundedWeeklySeriesCreatedInGoogleInAnotherTimeZoneHasReachedTheKiosk()
    {
        var state = State;

        ForeignTimeZoneId.Should().NotBe(
            state.Environment.Configuration.ExpectedTimeZone,
            "this scenario is about a series anchored to a zone that is NOT the family's. If the two are the "
            + "same, a write that substituted the family's zone would be indistinguishable from one that "
            + "preserved the series' own, and the scenario would pass while proving nothing");

        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);
        var firstDate = state.ReserveFirstDay(SmokeScenarioDays.Weekly(SmokeSeries.Occurrences));
        var rule = SmokeIcal.WeeklyOn([firstDate.DayOfWeek], SmokeSeries.Occurrences);

        var draft = new GoogleEventDraft(
            Summary: state.Correlation.Title("Series anchored elsewhere"),
            Start: ForeignBoundary(firstDate, ForeignStartTime),
            End: ForeignBoundary(firstDate, ForeignEndTime),
            Description: state.Correlation.Description("Bring the tickets and the flask"),
            Location: "Smoke Test Venue",
            ColorId: "5",
            Recurrence: [rule],
            Reminders: new GoogleEventReminders(
                UseDefault: false,
                Overrides: [new GoogleEventReminderOverride("popup", 45)]));

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.SeededRecurrenceRule = rule;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
        state.SeriesMasters = [new SmokeSeriesMaster(member, state.SeededGoogleEvent.Id)];

        await SeriesSteps.WaitForPreprodToHoldEveryOccurrenceAsync(
            state, "the series created in Google in another time zone");
    }

    [Then(@"Google holds the new title on that series")]
    public async Task ThenGoogleHoldsTheNewTitleOnThatSeries()
    {
        var state = State;

        await BoundedWait.UntilAsync(
            async () => (await SmokeSeries.MasterEventAsync(state)).Summary == state.ExpectedTitle,
            "Google never received the kiosk's title change for this series. A scenario that only proved "
            + "nothing else changed would pass for a write that never went out at all",
            TimeSpan.FromSeconds(state.Environment.Configuration.GoogleWaitSeconds));
    }

    [Then(@"Google still holds that series' own time zone, its rule and every other field")]
    public async Task ThenGoogleStillHoldsThatSeriesOwnTimeZoneItsRuleAndEveryOtherField()
    {
        var state = State;
        var original = state.RequireSeededGoogleEvent();

        // One read, so nothing below is compared against a different moment in time.
        var updated = await SmokeSeries.MasterEventAsync(state);

        updated.Start!.TimeZone.Should().Be(
            ForeignTimeZoneId,
            "a series is anchored to a zone, not to an instant, and the zone decides where each later "
            + "occurrence falls across a daylight-saving change. FamilyHQ's configured zone is a FALLBACK "
            + "for a series Google gave no zone for — never a replacement for one it did. Substituting it "
            + "re-anchors every future occurrence, and the family sees the damage in the Google Calendar "
            + "app months from now");

        updated.End!.TimeZone.Should().Be(
            ForeignTimeZoneId, "the end is anchored to the same zone as the start, or the duration drifts");

        // Against the rule GOOGLE returned for the insert, not the one this scenario composed. Google
        // normalises an RRULE's parts into its own order, so the string it stores is not byte-for-byte the
        // string it was sent — and the question the golden rule asks is whether the value Google held before
        // the edit is the value it holds after it, not whether it matches something assembled here.
        SmokeSeries.RequireSingleRule(updated).Should().Be(
            SmokeSeries.RequireSingleRule(original),
            "a title change is not a change of rule. A rewritten rule changes which days the series falls "
            + "on, which is the whole series moved by an edit that asked for a rename");

        updated.Description.Should().Contain(
            "Bring the tickets and the flask",
            "the user's own description text must survive an edit that never touched the description");
        updated.Description.Should().Contain(
            state.Correlation.DescriptionMarker,
            "the rest of the description must survive too, not just its first line");

        updated.Location.Should().Be(
            original.Location, "the location was not edited, so it must come back exactly as it was");

        updated.ColorId.Should().Be(
            original.ColorId, "FamilyHQ has no opinion about an event's colour and must not clear it");

        SmokeSeries.StartOf(updated).Should().Be(
            SmokeSeries.StartOf(original), "the series' origin was not edited, so it must not move");
        SmokeSeries.EndOf(updated).Should().Be(
            SmokeSeries.EndOf(original), "nor may its duration change");

        updated.Reminders!.UseDefault.Should().Be(
            original.Reminders!.UseDefault,
            "a series that opted out of the calendar's default reminders must stay opted out");
        updated.Reminders.Overrides.Should().BeEquivalentTo(
            original.Reminders.Overrides,
            "the reminder the phone set is the one the family will be shown; a title edit must not move it");
    }

    private static GoogleEventDateTime ForeignBoundary(DateOnly date, TimeOnly time) =>
        new(
            SmokeZones.ToOffset(ForeignTimeZoneId, date.ToDateTime(time)),
            Date: null,
            TimeZone: ForeignTimeZoneId);
}
