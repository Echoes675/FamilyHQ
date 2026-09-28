using System.Globalization;
using FamilyHQ.Smoke.Common.Pages;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// The all-day round trip, in both directions.
/// <para>
/// An all-day event is not "a timed event that happens to last a day": Google models it as a pair of
/// calendar dates whose end is exclusive, with no instant and no zone anywhere. Both halves of the round
/// trip are checked against Google's own representation, because the failures here are a silent day-shift
/// and a silent extra day — neither of which looks wrong in FamilyHQ's own answer about itself.
/// </para>
/// </summary>
[Binding]
public sealed class AllDayEventSteps(ScenarioContext scenarioContext)
{
    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    [When(@"I create a one-day all-day event on the kiosk")]
    public async Task WhenICreateAOneDayAllDayEventOnTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var date = state.EventDay;

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("All-day outing"),
            Description: state.Correlation.Description("One-day all-day event created by the smoke suite"),
            CalendarNames: [member],
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            IsAllDay: true);

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google holds it as a start date with the end date on the following day")]
    public async Task ThenGoogleHoldsItAsAStartDateWithTheEndDateOnTheFollowingDay()
    {
        var state = State;
        var day = state.EventDate;

        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            day,
            candidates => candidates.Count == 1,
            "Google never settled on exactly one event carrying this scenario's correlation marker for the "
            + "all-day event the kiosk created");

        var stored = found.Single().Event;

        stored.Start!.Date.Should().Be(
            day.ToString(SmokeEventShape.GoogleDateFormat, CultureInfo.InvariantCulture),
            "an all-day event's start is the first day it covers, as a bare calendar date");

        stored.End!.Date.Should().Be(
            SmokeEventShape.ExclusiveEndDate(day)
                .ToString(SmokeEventShape.GoogleDateFormat, CultureInfo.InvariantCulture),
            "Google's all-day end date is EXCLUSIVE — the day after the last day the event covers. Writing "
            + "the last day itself back describes an event that ends before it starts, and Google rejects "
            + "it; writing a day later gives the family a two-day event they never asked for");
    }

    [Then(@"Google holds no time and no time zone for it")]
    public async Task ThenGoogleHoldsNoTimeAndNoTimeZoneForIt()
    {
        var state = State;
        var found = await SmokeLookup.FindInGoogleAsync(state, state.EventDate);
        var stored = found.Should().ContainSingle().Subject.Event;

        stored.Start!.DateTime.Should().BeNull(
            "an all-day event carries a date and not an instant; a dateTime alongside it is a different "
            + "kind of event, and the Google Calendar app would draw it in a time slot");
        stored.End!.DateTime.Should().BeNull("the same is true of the end");

        stored.Start.TimeZone.Should().BeNull(
            "an all-day event is anchored to a date, so a daylight-saving change cannot move it and there "
            + "is nothing for a zone to mean. Sending one anyway would be FamilyHQ asserting something "
            + "about the event that the family never stated");
    }

    [Given(@"a one-day all-day event is created in Google on a member's calendar")]
    public async Task GivenAOneDayAllDayEventIsCreatedInGoogleOnAMembersCalendar()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        var draft = SmokeEventShape.PhoneStyleAllDayDraft(
            state.Correlation,
            "Phone-made all-day event",
            "One-day all-day event created directly in Google",
            state.EventDay);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = state.EventDay;
        state.ExpectedTitle = draft.Summary;
    }

    [Then(@"preprod serves it as an all-day event ending at the next day's boundary")]
    public async Task ThenPreprodServesItAsAnAllDayEventEndingAtTheNextDaysBoundary()
    {
        var state = State;
        var day = state.EventDate;

        var served = await SmokeLookup.WaitForPreprodAsync(
            state,
            day,
            candidates => candidates.Count == 1,
            "preprod never settled on exactly one occurrence for the all-day event created in Google");

        var occurrence = served.Single();

        occurrence.IsAllDay.Should().BeTrue(
            "an event Google described with dates rather than instants is an all-day event, and the kiosk "
            + "draws the two differently");

        DateOnly.FromDateTime(occurrence.Start.UtcDateTime).Should().Be(
            day, "the first day Google named is the day the event starts on");

        DateOnly.FromDateTime(occurrence.End.UtcDateTime).Should().Be(
            SmokeEventShape.ExclusiveEndDate(day),
            "the exclusive end date Google sent must survive as an exclusive end. Turning it into an "
            + "inclusive one here is how a one-day event becomes a two-day event, and the write-back then "
            + "tells Google the same lie");
    }

    [Then(@"the kiosk shows it on that day and not on the next")]
    public async Task ThenTheKioskShowsItOnThatDayAndNotOnTheNext()
    {
        var state = State;
        var dashboard = state.RequireDashboard();
        var shortId = state.Correlation.ShortId;

        await dashboard.WaitForEventOnDayAsync(shortId, state.EventDate);

        await dashboard.GoToDayAsync(state.EventDate.AddDays(1));
        var titlesOnTheNextDay = await dashboard.GetDayViewTitlesAsync();

        titlesOnTheNextDay.Should().NotContain(
            title => title.Contains(shortId, StringComparison.Ordinal),
            "the exclusive end date names the day AFTER the event, so a one-day all-day event must not also "
            + "appear on it. A family reading the kiosk would otherwise see the event lasting twice as long "
            + "as the phone says it does");
    }
}
