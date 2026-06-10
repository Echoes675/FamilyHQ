using FamilyHQ.E2E.Common.Pages;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// FHQ-63: step bindings for the kiosk day-rollover E2E scenarios.
// Each scenario navigates to a fresh state via the Background login, so the scenarios are
// fully order-independent (memory: feedback_e2e_isolation).
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
        var target = DateTime.Today.AddDays(days).ToString(
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
