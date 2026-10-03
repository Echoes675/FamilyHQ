using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// Step bindings for the kiosk day-rollover scenarios: an idle dashboard pulls the displayed date
// back to today. Each scenario navigates to a fresh state via the Background login, so the
// scenarios are fully order-independent (memory: feedback_e2e_isolation).
//
// Fifteen idle minutes also send the kiosk home to the reminders timeline, which is a separate rule
// with its own feature file. It is why the scenarios here re-select the view under test after the
// idle check: by then it is not the view on screen. See DayRollover.feature's own prose for what
// that does and does not leave each assertion able to prove.
[Binding]
public class DayRolloverSteps
{
    private readonly DashboardPage _dashboard;
    private readonly ScenarioContext _scenarioContext;

    // Captured "before" labels used by the Then assertions.
    private string _dayHeaderBeforeRollover = string.Empty;
    private string _monthLabelBeforeRollover = string.Empty;

    public DayRolloverSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _dashboard = new DashboardPage(scenarioContext.Get<IPage>());
    }

    // ── Given steps ──────────────────────────────────────────────────────────

    [Given("I am on the Day view showing today")]
    public async Task OnDayViewToday()
    {
        await _dashboard.NavigateAndWaitAsync();
        await _dashboard.SwitchToDayViewAsync();
        _dayHeaderBeforeRollover = (await _dashboard.GetDayHeaderTextAsync()).Trim();
    }

    [Given("I am on the Month view showing the current month")]
    public async Task OnMonthViewCurrent()
    {
        await _dashboard.NavigateAndWaitAsync();
        await _dashboard.SwitchToMonthViewAsync();
        _monthLabelBeforeRollover = (await _dashboard.GetMonthHeaderTextAsync()).Trim();
    }

    [Given("I am on the Agenda view showing the current month")]
    public async Task OnAgendaViewCurrent()
    {
        await _dashboard.NavigateAndWaitAsync();
        await _dashboard.SwitchToAgendaViewAsync();
        _monthLabelBeforeRollover = (await _dashboard.GetAgendaMonthYearTextAsync()).Trim();
    }

    [Given(@"I am on the Day view navigated {int} days into the future")]
    public async Task OnDayViewFuture(int days)
    {
        await _dashboard.NavigateAndWaitAsync();
        await _dashboard.SwitchToDayViewAsync();
        // Capture today's header before navigating forward so the snap-back assertion can
        // compare against it.
        _dayHeaderBeforeRollover = (await _dashboard.GetDayHeaderTextAsync()).Trim();
        var target = BrowserClock.Today.AddDays(days).ToString(
            "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await _dashboard.OpenDayPickerAndGoAsync(target);
    }

    [Given("the create-event modal is open")]
    public Task OpenCreateModal() => _dashboard.OpenCreateEventModalAsync();

    // ── When steps ───────────────────────────────────────────────────────────

    [When(@"the kiosk has been idle for {int} minutes")]
    public Task IdleFor(int minutes) => _dashboard.ForceIdleMinutesAsync(minutes);

    [When(@"the user interacted {int} minutes ago")]
    public Task InteractedAgo(int minutes) => _dashboard.ForceIdleMinutesAsync(minutes);

    [When(@"the date rolls over by {int} day")]
    public Task RolloverDays(int days) => _dashboard.AdvanceClockDaysAsync(days);

    [When(@"the date rolls over by {int} month")]
    public Task RolloverMonth(int months) =>
        // Advancing 31 days guarantees a month boundary is crossed regardless of when in the month
        // the test runs (the shortest month is 28 days).
        _dashboard.AdvanceClockDaysAsync(31 * months);

    [When("the idle check runs")]
    public Task RunIdleCheck() => _dashboard.RunIdleCheckAsync();

    [When("I cancel the event modal")]
    public Task CancelModal() => _dashboard.CancelEventModalAsync();

    // ── Then steps ───────────────────────────────────────────────────────────
    //
    // Each of these reads a label off the view under test, which after an idle check means the
    // scenario has re-selected that view with a tab step first. Two things follow from that, and
    // both are load-bearing:
    //
    // The tap is also a real interaction, so it re-stamps idle.js's monotonic clock. Without it the
    // 30-second poll would keep finding the kiosk idle and keep sending it home, and these waits
    // would be racing a view that leaves again mid-assertion. The forced idle a scenario sets is
    // deliberately NOT re-forced after the tap for that reason.
    //
    // What the re-selection costs the assertion differs by view. The Month and Agenda tabs do not
    // touch the displayed month, so their labels still read what the snap put there. The Day View
    // tab opens on today by design when tapped without a date, so DayShowsNewDay and DayShowsToday
    // below can no longer fail on a snap that did not happen — only on a Day view that will not open
    // on the current day at all. Their scenarios assert the kiosk going home as a separate Then,
    // which is the half that can still fail for the original reason.

    [Then("the Day view shows the new current day")]
    public async Task DayShowsNewDay()
    {
        await Assertions.Expect(_dashboard.DayHeaderButton).Not.ToHaveTextAsync(
            _dayHeaderBeforeRollover, new() { Timeout = 10000 });
    }

    [Then("the Day view shows today")]
    public async Task DayShowsToday()
    {
        await Assertions.Expect(_dashboard.DayHeaderButton).ToHaveTextAsync(
            _dayHeaderBeforeRollover, new() { Timeout = 10000 });
    }

    [Then("the Day view still shows the previous day")]
    public async Task DayShowsPrevious()
    {
        await Assertions.Expect(_dashboard.DayHeaderButton).ToHaveTextAsync(
            _dayHeaderBeforeRollover, new() { Timeout = 10000 });
    }

    // These two keep their full original force: their tabs are read-only with respect to the
    // displayed month, so a snap that failed to move it still fails the assertion.

    [Then("the Month view shows the new current month")]
    public async Task MonthShowsNew()
    {
        await Assertions.Expect(_dashboard.MonthHeaderButton).Not.ToHaveTextAsync(
            _monthLabelBeforeRollover, new() { Timeout = 10000 });
    }

    [Then("the Agenda view shows the new current month")]
    public async Task AgendaShowsNew()
    {
        await Assertions.Expect(_dashboard.AgendaMonthYearLabel).Not.ToHaveTextAsync(
            _monthLabelBeforeRollover, new() { Timeout = 10000 });
    }
}
