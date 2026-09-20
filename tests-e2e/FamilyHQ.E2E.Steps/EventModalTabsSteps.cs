using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

// FHQ-199: the event editor is tabbed (Details · Repeat). These steps cover what the tabs add —
// which tab is showing, and that a tab reports its own state so nothing hides on an inactive one.
// Driving the recurrence picker itself stays in RecurringEventSteps.
[Binding]
public class EventModalTabsSteps
{
    private readonly DashboardPage _dashboardPage;

    public EventModalTabsSteps(ScenarioContext scenarioContext)
    {
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    [When(@"I show the ""([^""]*)"" tab of the event editor")]
    public async Task WhenIShowTheTabOfTheEventEditor(string tabName)
    {
        await _dashboardPage.ShowModalTabAsync(tabName.ToLowerInvariant());
    }

    [Then(@"the event editor is showing the ""([^""]*)"" tab")]
    public async Task ThenTheEventEditorIsShowingTheTab(string tabName)
    {
        await _dashboardPage.AssertModalTabActiveAsync(tabName.ToLowerInvariant());
    }

    [Then(@"the Repeat tab is marked as incomplete")]
    public async Task ThenTheRepeatTabIsMarkedAsIncomplete()
    {
        await _dashboardPage.AssertRepeatTabIncompleteAsync();
    }

    [Then(@"the event editor explains that the repeat settings must be finished before saving")]
    public async Task ThenTheEventEditorExplainsThatRepeatMustBeFinished()
    {
        await _dashboardPage.AssertSaveHintVisibleAsync();
    }

    [Then(@"the Repeat tab is labelled ""([^""]*)""")]
    public async Task ThenTheRepeatTabIsLabelled(string expected)
    {
        await _dashboardPage.AssertRepeatTabBadgeAsync(expected);
    }

    // FHQ-199 Fix 2: the Details tab flags its own unmet requirement (no calendar selected) so the
    // marker is visible even while the Repeat tab is showing.

    [When(@"I attempt to save the event")]
    public async Task WhenIAttemptToSaveTheEvent()
    {
        await _dashboardPage.AttemptSaveAsync();
    }

    [When(@"I select the ""([^""]*)"" calendar for the event")]
    public async Task WhenISelectTheCalendarForTheEvent(string calendarName)
    {
        await _dashboardPage.EnsureCalendarChipActiveAsync(calendarName);
    }

    [Then(@"the Details tab is marked as incomplete")]
    public async Task ThenTheDetailsTabIsMarkedAsIncomplete()
    {
        await _dashboardPage.AssertDetailsTabIncompleteAsync();
    }

    [Then(@"the Details tab is not marked as incomplete")]
    public async Task ThenTheDetailsTabIsNotMarkedAsIncomplete()
    {
        await _dashboardPage.AssertDetailsTabNotIncompleteAsync();
    }

    // FHQ-199 Fix 1: the modal's bounding box must not change when the tab changes.

    [Then(@"the event editor stays in the same place as the tab changes")]
    public async Task ThenTheEventEditorStaysInTheSamePlaceAsTheTabChanges()
    {
        await _dashboardPage.AssertModalStaysStillAcrossTabsAsync();
    }
}
