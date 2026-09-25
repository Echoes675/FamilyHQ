using System.Globalization;
using FamilyHQ.Smoke.Common.Helpers;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.Smoke.Steps;

/// <summary>
/// RK1 and RG1 — recurrence, checked against Google's own expansion and never against a calculation of
/// ours (FHQ-141 principle 6).
/// <para>
/// This is the one area where "two implementations that agree in the common case" is not good enough. A
/// weekly rule and a locally-derived weekly rule diverge at a DST transition, and the divergence has a date
/// on it. <c>events.instances</c> is Google telling us what the occurrences are; whatever it returns is
/// correct by definition, and preprod either matches it or does not.
/// </para>
/// <para>
/// Every series created here is bounded — three occurrences, which is enough to prove a weekly step and, for
/// two weekdays, enough to prove the rule wraps into the following week. The events are left behind for
/// post-mortem, so an unbounded series would keep generating occurrences on a live calendar for ever.
/// </para>
/// </summary>
[Binding]
public sealed class RecurrenceSteps(ScenarioContext scenarioContext)
{
    private const int Occurrences = 3;

    private static readonly DayOfWeek FirstWeekday = DayOfWeek.Tuesday;
    private static readonly DayOfWeek SecondWeekday = DayOfWeek.Thursday;

    private SmokeScenarioState State => scenarioContext.Get<SmokeScenarioState>();

    // ── RK1: kiosk → Google ─────────────────────────────────────────────────────

    [When(@"I create a bounded weekly series on the kiosk")]
    public async Task WhenICreateABoundedWeeklySeriesOnTheKiosk()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var date = SmokeEventShape.Date;

        var draft = new SmokeEventDraft(
            Title: state.Correlation.Title("Weekly series from the kiosk"),
            Description: state.Correlation.Description("Bounded weekly series created by the smoke suite"),
            CalendarNames: [member],
            Date: date,
            StartTime: SmokeEventShape.StartTime,
            EndTime: SmokeEventShape.EndTime,
            Recurrence: new SmokeWeeklyRecurrence([date.DayOfWeek], Occurrences));

        await state.RequireDashboard().CreateEventAsync(draft);

        state.Draft = draft;
        state.MemberNames = [member];
        state.EventDate = date;
        state.ExpectedTitle = draft.Title;
    }

    [Then(@"Google holds one series master, bounded weekly, anchored to the family time zone")]
    public async Task ThenGoogleHoldsOneSeriesMasterBoundedWeeklyAnchoredToTheFamilyTimeZone()
    {
        var state = State;
        var expectedZone = state.Environment.Configuration.ExpectedTimeZone;

        // singleEvents=false, so Google returns the master and its recurrence array rather than the expansion.
        var found = await SmokeLookup.WaitForGoogleAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == 1 && candidates[0].Event.Recurrence?.Count > 0,
            "Google never settled on exactly one recurring master for this scenario. More than one means the "
            + "kiosk wrote the series twice; a master with no recurrence array means it was written as a "
            + "single event and the rule was lost");

        var master = found.Single().Event;
        var rules = master.Recurrence!.Where(line => line.StartsWith("RRULE:", StringComparison.Ordinal)).ToList();

        rules.Should().ContainSingle("a series has exactly one RRULE; a second would change what it means");
        rules[0].Should().Contain("FREQ=WEEKLY", "the kiosk was asked for a weekly series");
        rules[0].Should().Contain(
            $"COUNT={Occurrences}",
            "the series must be bounded by the count the kiosk was given; the smoke events are kept, so an "
            + "endless series would keep expanding on a live calendar");
        rules[0].Should().NotContain(
            "UNTIL=",
            "a COUNT rule and an UNTIL rule are different rules — Google would expand them differently");

        master.Start!.TimeZone.Should().Be(
            expectedZone,
            "a series is anchored to a zone, not to an instant: the zone decides where each later occurrence "
            + "falls across a DST transition. Writing anything other than the family's zone re-anchors every "
            + "future occurrence (FHQ-170)");
    }

    // ── RG1: Google → kiosk ─────────────────────────────────────────────────────

    [StepDefinition(@"a bounded weekly series on two weekdays is created in Google")]
    public async Task ABoundedWeeklySeriesOnTwoWeekdaysIsCreatedInGoogle()
    {
        var state = State;
        var member = state.Environment.Configuration.MemberCalendarNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(member);

        // The first occurrence lands on the first of the two weekdays, so Google's expansion starts where the
        // rule says it should rather than on whatever day the run happens to be.
        var firstDate = FamilyClock.NextWeekdayAfterToday(FirstWeekday);

        var rule = "RRULE:FREQ=WEEKLY"
                   + $";BYDAY={IcalDay(FirstWeekday)},{IcalDay(SecondWeekday)}"
                   + $";COUNT={Occurrences}";

        var draft = SmokeEventShape.PhoneStyleDraft(
            state.Correlation,
            "Weekly series from Google",
            "Bounded two-weekday series created directly in Google",
            firstDate,
            recurrence: [rule]);

        state.SeededGoogleEvent = await state.Environment.Google.InsertEventAsync(calendarId, draft);
        state.SeededCalendarName = member;
        state.MemberNames = [member];
        state.EventDate = firstDate;
        state.ExpectedTitle = draft.Summary;
    }

    [Then(@"the occurrences preprod serves are exactly Google's instances")]
    public async Task ThenTheOccurrencesPreprodServesAreExactlyGooglesInstances()
    {
        var state = State;
        var instances = await GoogleInstancesAsync(state);

        instances.Should().HaveCount(
            Occurrences, "Google is the oracle for how many occurrences a bounded rule produces");

        var expected = instances.Select(StartInstant).OrderBy(instant => instant).ToList();

        var served = await SmokeLookup.WaitForPreprodAsync(
            state,
            state.EventDate,
            candidates => candidates.Count == expected.Count,
            $"preprod never settled on {expected.Count} occurrences for this series. Google expands the rule "
            + $"to {expected.Count}; a different number from preprod means the two disagree about what the "
            + "rule means, which is a latent bug with a date on it");

        served.Select(occurrence => occurrence.Start.UtcDateTime)
            .OrderBy(instant => instant)
            .Should().Equal(
                expected,
                "every occurrence must fall on the instant Google says it does — not merely the right number "
                + "of them on roughly the right days");
    }

    [Then(@"each occurrence preprod serves is at the same wall-clock time as Google's")]
    public async Task ThenEachOccurrencePreprodServesIsAtTheSameWallClockTimeAsGooglesAsync()
    {
        var state = State;
        var instances = await GoogleInstancesAsync(state);
        var served = await SmokeLookup.FindInPreprodAsync(state, state.EventDate);

        var googleWallClock = instances
            .Select(instance => FamilyClock.ToFamilyWallClock(StartOffset(instance)))
            .Select(wallClock => wallClock.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        var preprodWallClock = served
            .Select(occurrence => FamilyClock.ToFamilyWallClock(occurrence.Start))
            .Select(wallClock => wallClock.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        preprodWallClock.Should().Equal(
            googleWallClock,
            "a family reads the kiosk in wall-clock time. Two expansions that agree on the instant but "
            + "disagree on the displayed hour would show the same event at different times on the phone and "
            + "on the wall");
    }

    [Then(@"the kiosk marks those occurrences as recurring")]
    public async Task ThenTheKioskMarksThoseOccurrencesAsRecurring()
    {
        var state = State;
        var dashboard = state.RequireDashboard();

        await dashboard.WaitForEventOnDayAsync(state.Correlation.ShortId, state.EventDate);

        var marked = await dashboard.TileIsMarkedRecurringAsync(state.Correlation.ShortId);

        marked.Should().BeTrue(
            "an occurrence the family cannot tell is part of a series is one they will edit expecting to "
            + "change only that day");
    }

    private async Task<IReadOnlyList<GoogleEvent>> GoogleInstancesAsync(SmokeScenarioState state)
    {
        var calendarName = state.SeededCalendarName ?? state.MemberNames.First();
        var calendarId = state.Environment.Calendars.RequireGoogleId(calendarName);
        var (from, to) = SmokeLookup.WindowAround(state.EventDate);

        var masterId = state.SeededGoogleEvent?.Id;
        if (masterId is null)
        {
            var found = await SmokeLookup.FindInGoogleAsync(state, state.EventDate);
            masterId = found.Should().ContainSingle(
                "the series master is the only event carrying this scenario's marker when instances are not "
                + "expanded").Subject.Event.Id;
        }

        return await state.Environment.Google.ListInstancesAsync(calendarId, masterId, from, to);
    }

    private static DateTime StartInstant(GoogleEvent instance) => StartOffset(instance).UtcDateTime;

    private static DateTimeOffset StartOffset(GoogleEvent instance) =>
        instance.Start?.DateTime
        ?? throw new InvalidOperationException(
            $"Google instance {instance.Id} has no dateTime start. The smoke series are timed, not all-day, "
            + "so an all-day boundary here means the series was not created as this suite intended.");

    private static string IcalDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        DayOfWeek.Sunday => "SU",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Not a day of the week.")
    };
}
