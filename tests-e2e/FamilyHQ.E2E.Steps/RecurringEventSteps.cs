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
        subtitle.Should().Be(expected,
            "the opened recurring instance must describe its weekly repeat pattern.");
    }
}
