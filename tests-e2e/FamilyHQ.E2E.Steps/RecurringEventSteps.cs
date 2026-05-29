using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// FHQ-18.11: assertions on the observable recurrence affordances — the per-instance expansion
// count, the recurrence indicator glyph rendered on recurring tiles, and the plain-English
// recurrence subtitle shown when a recurring instance is opened.
[Binding]
public class RecurringEventSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly DashboardPage _dashboardPage;

    public RecurringEventSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    [Then(@"I see (\d+) occurrences of the event ""([^""]*)"" on the calendar")]
    public async Task ThenISeeOccurrencesOfTheEventOnTheCalendar(int expected, string eventName)
    {
        var count = await _dashboardPage.WaitForEventInstanceCountAsync(eventName, expected);
        count.Should().Be(expected,
            $"the recurring series '{eventName}' should expand into {expected} instances on the calendar.");
    }

    // FHQ-18.11: window-independent expansion check. Rather than counting capsules in the fixed
    // 6-week month grid (which under-counts when the series starts late in the month and later
    // occurrences fall past the visible edge), this drives the Day view to each weekly occurrence
    // date in turn and asserts the tile is present there. Occurrence dates are derived from the
    // seeded first-occurrence date, so the assertion holds for any run date.
    [Then(@"the event ""([^""]*)"" appears on each of its (\d+) weekly occurrence dates")]
    public async Task ThenTheEventAppearsOnEachOfItsWeeklyOccurrenceDates(string eventName, int occurrences)
    {
        var firstOccurrence = _scenarioContext.Get<System.DateTime>("RecurringSeriesFirstOccurrenceDate");
        await _dashboardPage.AssertWeeklyOccurrencesEachVisibleInDayViewAsync(
            eventName, firstOccurrence, occurrences);
    }

    [Then(@"the recurring event shows a recurrence indicator")]
    public async Task ThenTheRecurringEventShowsARecurrenceIndicator()
    {
        await _dashboardPage.WaitForRecurrenceIndicatorVisibleAsync();
        var count = await _dashboardPage.CountRecurrenceIndicatorsAsync();
        count.Should().BeGreaterThan(0,
            "recurring event tiles must display the recurrence indicator glyph.");
    }

    [Then(@"the recurring event details describe the weekly repeat pattern")]
    public async Task ThenTheRecurringEventDetailsDescribeTheWeeklyRepeatPattern()
    {
        var expected = _scenarioContext.Get<string>("RecurringSeriesExpectedSubtitle");
        var subtitle = await _dashboardPage.GetRecurrenceSubtitleTextAsync();
        // Assert the human-readable weekly pattern is present rather than exact-matching: the
        // describer also appends the end condition (e.g. ", 3 times") for a COUNT-bounded series,
        // which is not what this scenario is asserting.
        subtitle.Should().Contain(expected,
            "the opened recurring instance must describe its weekly repeat pattern.");
    }

    // ── FHQ-18.11 Pass 2: native create + toggle-off ──────────────────────────

    // Creates a weekly recurring event through the FamilyHQ create modal (Weekly preset → repeats
    // on today's weekday, the Add Event modal's default start). The app writes an events.insert with
    // a recurrence array to the Simulator and reconciles, so the instances appear after the reload.
    // First occurrence is today (relative-date safe), recorded so the reused per-date Day-view
    // assertion ("appears on each of its N weekly occurrence dates") can derive the occurrence dates.
    [When(@"I create a weekly recurring event ""([^""]*)"" in ""([^""]*)""")]
    public async Task WhenICreateAWeeklyRecurringEvent(string eventName, string calendarName)
    {
        await _dashboardPage.CreateWeeklyRecurringEventAsync(eventName, calendarName);
        _scenarioContext["RecurringSeriesFirstOccurrenceDate"] = System.DateTime.Today;
    }

    // Creates a recurring event repeating weekly on a specific weekday: the weekday that falls
    // <days> days from today. Using the Custom drawer's weekday toggle proves the BYDAY selection
    // reaches the series. The first occurrence is that weekday's date (>= today, so window-safe).
    [When(@"I create a recurring event ""([^""]*)"" in ""([^""]*)"" repeating weekly on the day in (\d+) days")]
    public async Task WhenICreateARecurringEventOnAChosenWeekday(string eventName, string calendarName, int days)
    {
        var targetDate = System.DateTime.Today.AddDays(days);
        await _dashboardPage.CreateCustomWeeklyRecurringEventAsync(
            eventName, calendarName, new[] { targetDate.DayOfWeek.ToString() });
        _scenarioContext["ChosenWeekdayFirstOccurrenceDate"] = targetDate;
    }

    // Asserts the chosen-weekday series renders one instance on the target weekday's date and the
    // week after, via the Day view (window-independent, mirrors Pass 1's per-date approach).
    [Then(@"the event ""([^""]*)"" appears weekly starting in (\d+) days for (\d+) occurrences")]
    public async Task ThenTheEventAppearsWeeklyStartingInDays(string eventName, int days, int occurrences)
    {
        var firstOccurrence = System.DateTime.Today.AddDays(days);
        await _dashboardPage.AssertWeeklyOccurrencesEachVisibleInDayViewAsync(
            eventName, firstOccurrence, occurrences);
    }

    // Opens an existing recurring event, sets it to "Does not repeat", saves and confirms the
    // recurrence-scope prompt — the app sends an empty recurrence array (events.patch clear) so the
    // Simulator collapses the series. The clear reconciles to a single non-recurring event on the
    // series' first occurrence date (today, the native-create default).
    [When(@"I turn off recurrence for the event ""([^""]*)""")]
    public async Task WhenITurnOffRecurrenceForTheEvent(string eventName)
    {
        await _dashboardPage.TurnOffRecurrenceForEventAsync(eventName);
    }

    [Then(@"only a single non-recurring ""([^""]*)"" event remains")]
    public async Task ThenOnlyASingleNonRecurringEventRemains(string eventName)
    {
        await _dashboardPage.AssertSingleNonRecurringOccurrenceInDayViewAsync(
            eventName, System.DateTime.Today);
    }

    // ── FHQ-18.11 Pass 3: edit-scope (This event / This and following / All events) ───────────

    // Opens the Nth weekly occurrence of the series on the Day view, renames it, saves and confirms
    // the recurrence-scope prompt with the chosen scope. Occurrence dates are derived from the seeded
    // first-occurrence date (+7-day steps), so the step is run-date independent. Scope word is one of
    // "this" / "following" / "all" — the recurrence-scope-* testid suffixes.
    [When(@"I change occurrence (\d+) of ""([^""]*)"" to ""([^""]*)"" applying to ""([^""]*)"" scope")]
    public async Task WhenIChangeOccurrenceToApplyingScope(
        int occurrence, string seriesName, string newTitle, string scope)
    {
        var date = OccurrenceDate(occurrence);
        await _dashboardPage.EditRecurringOccurrenceTitleWithScopeAsync(seriesName, date, newTitle, scope);
    }

    // Establishes a pre-existing per-instance exception override before an all-events edit, so the
    // all-events scenario can prove the override is preserved. This is a "This event" edit applied to
    // the Nth occurrence.
    [Given(@"occurrence (\d+) of ""([^""]*)"" has already been changed to ""([^""]*)""")]
    public async Task GivenOccurrenceHasAlreadyBeenChangedTo(int occurrence, string seriesName, string newTitle)
    {
        var date = OccurrenceDate(occurrence);
        await _dashboardPage.EditRecurringOccurrenceTitleWithScopeAsync(seriesName, date, newTitle, "this");
    }

    // Asserts the named event is shown on the Nth occurrence's date in the Day view. Used for both the
    // changed occurrence and (with "still appears") an untouched occurrence retaining its title.
    [Then(@"the event ""([^""]*)"" (?:appears|still appears) on occurrence (\d+)")]
    public async Task ThenTheEventAppearsOnOccurrence(string eventName, int occurrence)
    {
        var date = OccurrenceDate(occurrence);
        await _dashboardPage.AssertEventVisibleInDayViewOnDateAsync(eventName, date);
    }

    // Occurrence N (1-based) of the weekly series falls N-1 weeks after the seeded first occurrence.
    private System.DateTime OccurrenceDate(int occurrence)
    {
        var first = _scenarioContext.Get<System.DateTime>("RecurringSeriesFirstOccurrenceDate");
        return first.Date.AddDays(7 * (occurrence - 1));
    }
}
