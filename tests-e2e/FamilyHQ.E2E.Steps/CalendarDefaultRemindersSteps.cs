using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Drives the Simulator backdoor that sets a calendar's Google-side default reminders, and reads back
/// what the event modal's Reminders tab says about them.
/// </summary>
/// <remarks>
/// The backdoor exists because CI has no aged data: a calendar's defaults are stored at creation, so
/// without a change mid-run the refresh path early-returns and the write that once took production
/// down is never reached.
/// <para>
/// It also distinguishes the two answers that matter here. An EMPTY <c>defaultReminders</c> array is
/// Google saying the calendar has no defaults; an ABSENT one is Google saying nothing, which is the
/// state every calendar starts in and what a stored null means. The tab has to tell them apart,
/// because only the first may be described to a family as notifying nobody.
/// </para>
/// </remarks>
[Binding]
public class CalendarDefaultRemindersSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;

    public CalendarDefaultRemindersSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    // The Given form is the same backdoor call made as a precondition: a calendar whose usual
    // reminders Google already reports. It has to run BEFORE the login that triggers the first sync
    // (which is what adopts them) and AFTER any step that seeds events, because seeding re-posts the
    // user template and the Simulator rebuilds the calendar rows from it.
    [Given(@"the active calendar's usual reminders in Google are (\d+) minutes")]
    [When(@"the active calendar's default reminders change to (\d+) minutes in Google")]
    public async Task WhenTheActiveCalendarsDefaultRemindersChangeTo(int minutes)
    {
        // CurrentCalendarId is the scenario's own uniquely-suffixed id, set by
        // "the ""X"" calendar is the active calendar". Never hard-code an id: scenarios run in
        // parallel against one Simulator and must not touch each other's data.
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        await _simulatorApi.SetCalendarDefaultRemindersAsync(
            calendarId,
            new[] { new { method = "popup", minutes } });
    }

    // An EMPTY array, which is not the same call as clearing the defaults: clearing sends null and
    // Google then omits the array altogether, which is "nothing has reported them". Same ordering
    // rule as above — before the login that adopts it.
    [Given(@"the active calendar has no usual reminders in Google")]
    public async Task GivenTheActiveCalendarHasNoUsualRemindersInGoogle()
    {
        await _simulatorApi.SetCalendarDefaultRemindersAsync(
            _scenarioContext.GetCurrentCalendarId(),
            Array.Empty<object>());
    }

    [Then(@"the Reminders tab says this calendar has no usual reminders")]
    public async Task ThenTheRemindersTabSaysThisCalendarHasNoUsualReminders()
    {
        await _dashboardPage.AssertCalendarHasNoUsualRemindersStatedAsync();
    }

    [Then(@"the Reminders tab says this calendar's usual reminders can't be read")]
    public async Task ThenTheRemindersTabSaysThisCalendarsUsualRemindersCannotBeRead()
    {
        await _dashboardPage.AssertCalendarUsualRemindersUnreadableStatedAsync();
    }
}
