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
}
