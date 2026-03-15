using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class DashboardSteps
{
    private readonly DashboardPage _dashboardPage;
    private readonly ScenarioContext _scenarioContext;

    public DashboardSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(page);
    }

    [Given(@"I view the dashboard")]
    [When(@"I view the dashboard")]
    public async Task WhenIViewTheDashboard()
    {
        await _dashboardPage.NavigateAndWaitAsync();
    }

    [Then(@"I see the event ""([^""]*)"" displayed on the calendar")]
    public async Task ThenISeeTheEventDisplayedOnTheCalendar(string eventName)
    {
        var visibleEvents = await _dashboardPage.GetVisibleEventsAsync();
        
        visibleEvents.Should().Contain(e => e.Contains(eventName), 
            $"The event '{eventName}' should be visible on the dashboard month view.");
    }

    [Then(@"I do not see the event ""([^""]*)"" displayed on the calendar")]
    public async Task ThenIDoNotSeeTheEventDisplayedOnTheCalendar(string eventName)
    {
        var visibleEvents = await _dashboardPage.GetVisibleEventsAsync();
        
        visibleEvents.Should().NotContain(e => e.Contains(eventName), 
            $"The event '{eventName}' should NOT be visible on the dashboard month view.");
    }

    [When(@"I create an event ""([^""]*)""")]
    public async Task WhenICreateAnEvent(string eventName)
    {
        await _dashboardPage.CreateEventAsync(eventName);
    }

    [When(@"I rename the event ""([^""]*)"" to ""([^""]*)""")]
    public async Task WhenIRenameTheEventTo(string oldEventName, string newEventName)
    {
        await _dashboardPage.UpdateEventAsync(oldEventName, newEventName);
    }

    [When(@"I delete the event ""([^""]*)""")]
    public async Task WhenIDeleteTheEvent(string eventName)
    {
        await _dashboardPage.DeleteEventAsync(eventName);
    }

    [Given(@"I login as the user")]
    public void GivenILoginAsTheUser()
    {
        
    }

    [When(@"I click on the event ""([^""]*)""")]
    public async Task WhenIClickOnTheEvent(string eventName)
    {
        await _dashboardPage.ClickEventAsync(eventName);
    }

    [Then(@"I see the event details for ""([^""]*)""")]
    public async Task ThenISeeTheEventDetailsFor(string eventName)
    {
        var eventDetails = await _dashboardPage.GetEventDetailsAsync();
        eventDetails.Should().Contain(eventName, 
            $"The event details should contain '{eventName}'.");
    }

    [When(@"I navigate to the next month")]
    public async Task WhenINavigateToTheNextMonth()
    {
        await _dashboardPage.NavigateToNextMonthAsync();
    }
}
