using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

/// <summary>
/// Drives the kiosk's home view — which view a page load lands on, which tab comes first, and the
/// return an idle kiosk makes on its own.
/// </summary>
/// <remarks>
/// Only the steps unique to that behaviour live here. Getting idle, running the idle check, opening
/// the create-event modal and arriving on the Month view all already exist for the day-rollover
/// scenarios (<see cref="DayRolloverSteps"/>), and reading the timeline exists for the Reminders view
/// (<see cref="RemindersViewSteps"/>); a second copy of any of them would be a second definition of
/// the same words.
/// </remarks>
[Binding]
public class KioskHomeViewSteps
{
    private readonly DashboardPage _dashboard;

    public KioskHomeViewSteps(ScenarioContext scenarioContext)
    {
        _dashboard = new DashboardPage(scenarioContext.Get<IPage>());
    }

    /// <summary>
    /// Gives an event already on the grid a reminder of its own, through the modal, because there is
    /// no backdoor that seeds one: the Simulator's event model carries no reminder overrides, so the
    /// only way to reach the state this precondition describes is the write the kiosk itself makes.
    /// Inheritance goes off first — the Add form is not offered while an event follows its
    /// calendar's usual reminders.
    /// </summary>
    [Given(@"the event ""([^""]*)"" has been given a reminder (\d+) hours before")]
    public async Task GivenTheEventHasBeenGivenAReminderHoursBefore(string title, int hours)
    {
        await _dashboard.OpenEventForEditingAsync(title);
        await _dashboard.SetReminderInheritanceAsync(follow: false);
        await _dashboard.AddTimedReminderAsync(hours, "hours");
        await _dashboard.SaveOpenEventAsync();
    }

    /// <summary>
    /// Loads the dashboard as the kiosk does on power-on, tapping nothing, so the view that ends up
    /// on screen is the app's own choice rather than this step's.
    /// </summary>
    [When(@"the kiosk loads the dashboard")]
    public Task WhenTheKioskLoadsTheDashboard() => _dashboard.LoadKioskHomeViewAsync();

    [Then(@"the dashboard's view tabs read ""([^""]*)""")]
    public async Task ThenTheDashboardsViewTabsRead(string expected)
    {
        // An ordered equality on the whole strip rather than a check that one tab is first: the
        // requirement is a tab ORDER, and asserting only the first position would pass on a strip
        // that had lost or reshuffled the other three.
        var labels = await _dashboard.ReadViewTabLabelsAsync();

        string.Join(", ", labels).Should().Be(
            expected,
            "the dashboard's tabs should read left to right in the order the family asked for.");
    }
}
