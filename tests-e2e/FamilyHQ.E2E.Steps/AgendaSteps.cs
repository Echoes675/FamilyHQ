using System.Globalization;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class AgendaSteps
{
    private readonly DashboardPage _dashboardPage;
    private readonly ScenarioContext _scenarioContext;
    private readonly IPage _page;

    public AgendaSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(_page);
    }

    /// <summary>
    /// Resolves the live calendar GUID by finding the column header whose text matches
    /// calendarName and extracting the GUID from its data-testid attribute.
    /// </summary>
    private async Task<Guid> ResolveCalendarIdFromPageAsync(string calendarName)
    {
        var header = _page.Locator("[data-testid^='agenda-calendar-header-']")
                          .Filter(new() { HasText = calendarName });
        var testId = await header.First.GetAttributeAsync("data-testid")
                     ?? throw new InvalidOperationException($"No header found for calendar '{calendarName}'.");
        // testid format: "agenda-calendar-header-{guid}"
        var guidStr = testId.Replace("agenda-calendar-header-", "");
        return Guid.Parse(guidStr);
    }

    // ─── Navigation ─────────────────────────────────────────────────────────────

    [When(@"I click the ""Agenda"" tab")]
    public async Task WhenIClickTheAgendaTab()
    {
        await _dashboardPage.SwitchToAgendaViewAsync();
    }

    [Then(@"I see the month agenda view")]
    public async Task ThenISeeTheMonthAgendaView()
    {
        await Assertions.Expect(_dashboardPage.AgendaViewContainer).ToBeVisibleAsync();
    }

    [When(@"I navigate to the previous month on the agenda view")]
    public async Task WhenINavigateToThePreviousMonthOnTheAgendaView()
    {
        await _dashboardPage.NavigateAgendaPrevMonthAsync();
    }

    [When(@"I navigate to the next month on the agenda view")]
    public async Task WhenINavigateToTheNextMonthOnTheAgendaView()
    {
        await _dashboardPage.NavigateAgendaNextMonthAsync();
    }

    [When(@"I navigate the agenda to ""([^""]*)""")]
    public async Task WhenINavigateTheAgendaTo(string targetMonthYear)
    {
        // Navigate prev/next until the label shows the target month
        var target = DateTime.ParseExact(targetMonthYear, "MMMM yyyy", CultureInfo.InvariantCulture);
        for (var i = 0; i < 24; i++) // max 24 steps to avoid infinite loop
        {
            var current = DateTime.ParseExact(
                await _dashboardPage.GetAgendaMonthYearTextAsync(), "MMMM yyyy", CultureInfo.InvariantCulture);
            if (current.Year == target.Year && current.Month == target.Month) break;
            if (current < target)
                await _dashboardPage.NavigateAgendaNextMonthAsync();
            else
                await _dashboardPage.NavigateAgendaPrevMonthAsync();
        }
    }

    [Then(@"the agenda view shows the previous month")]
    public async Task ThenTheAgendaViewShowsThePreviousMonth()
    {
        var expected = DateTime.Today.AddMonths(-1).ToString("MMMM yyyy");
        var actual = await _dashboardPage.GetAgendaMonthYearTextAsync();
        actual.Should().Be(expected);
    }

    [Then(@"the agenda view shows the next month")]
    public async Task ThenTheAgendaViewShowsTheNextMonth()
    {
        var expected = DateTime.Today.AddMonths(1).ToString("MMMM yyyy");
        var actual = await _dashboardPage.GetAgendaMonthYearTextAsync();
        actual.Should().Be(expected);
    }

    // ─── Display ────────────────────────────────────────────────────────────────

    [Then(@"the agenda view shows all days of the current month")]
    public async Task ThenTheAgendaViewShowsAllDaysOfTheCurrentMonth()
    {
        var monthText = await _dashboardPage.GetAgendaMonthYearTextAsync();
        var month = DateTime.ParseExact(monthText, "MMMM yyyy", CultureInfo.InvariantCulture);
        var expected = DateTime.DaysInMonth(month.Year, month.Month);
        var actual = await _dashboardPage.GetAgendaDayRowCountAsync();
        actual.Should().Be(expected, $"All {expected} days of {monthText} should be visible.");
    }

    [Then(@"weekend rows on the agenda view have the CSS class ""([^""]*)""")]
    public async Task ThenWeekendRowsHaveTheCssClass(string cssClass)
    {
        cssClass.Should().Be("agenda-weekend-row");
        var hasWeekends = await _dashboardPage.WeekendRowsHaveClassAsync();
        hasWeekends.Should().BeTrue("All Saturday/Sunday rows should have agenda-weekend-row.");
    }

    [Then(@"weekday rows on the agenda view do not have the class ""([^""]*)""")]
    public async Task ThenWeekdayRowsDoNotHaveTheClass(string cssClass)
    {
        cssClass.Should().Be("agenda-weekend-row");
        var weekdayCount = await _dashboardPage.GetWeekdayRowsWithoutWeekendClassAsync();
        weekdayCount.Should().BeGreaterThan(0, "There should be weekday rows without agenda-weekend-row.");
    }

    [Then(@"today's row on the agenda view has the CSS class ""([^""]*)""")]
    public async Task ThenTodaysRowHasTheCssClass(string cssClass)
    {
        cssClass.Should().Be("agenda-today-row");
        var hasToday = await _dashboardPage.HasTodayRowHighlightAsync();
        hasToday.Should().BeTrue("Today's row should have agenda-today-row.");
    }

    [Then(@"I see a column header for ""([^""]*)""")]
    public async Task ThenISeeAColumnHeaderFor(string calendarName)
    {
        var visible = await _dashboardPage.IsAgendaCalendarHeaderVisibleAsync(calendarName);
        visible.Should().BeTrue($"A column header for '{calendarName}' should be visible.");
    }

    [Then(@"I see the event ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenISeeTheEventInTheColumnFor(string expectedText, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        await Assertions.Expect(
            _page.GetByTestId($"agenda-cell-{dateKey}-{calId}")
                               .GetByText(expectedText, new() { Exact = false })
                               .First)
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Then(@"I do not see ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenIDoNotSeeInTheColumnFor(string text, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        // Use polling assertion to allow time for SignalR UI update after a webhook notification
        await Assertions.Expect(
            _page.GetByTestId($"agenda-cell-{dateKey}-{calId}")
                 .GetByText(text, new() { Exact = false })
                 .First)
            .ToBeHiddenAsync(new() { Timeout = 10000 });
    }

    [Then(@"the event ""([^""]*)"" has no time prefix in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenTheEventHasNoTimePrefixInTheColumnFor(string title, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        var cell = _page.GetByTestId($"agenda-cell-{dateKey}-{calId}");
        var eventLine = cell.GetByText(title, new() { Exact = false }).First;
        var text = await eventLine.InnerTextAsync();
        // Text should be just the title — no leading "HH:mm " pattern
        text.Trim().Should().Be(title, $"All-day event '{title}' should show title only, no time prefix.");
    }

    [Then(@"I see (\d+) event lines in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenISeeEventLinesInTheColumnFor(int count, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        var actual = await _dashboardPage.GetAgendaEventLineCountAsync(dateKey, calId);
        actual.Should().Be(count, $"Expected {count} event lines in {calendarName} on {dateKey}.");
    }

    [Then(@"I see a ""\+(\d+) more"" indicator in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenISeeAPlusNMoreIndicator(int n, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        var overflowText = await _dashboardPage.GetAgendaOverflowTextAsync(dateKey, calId);
        overflowText.Trim().Should().Be($"+{n} more");
    }

    // ─── Interactions ────────────────────────────────────────────────────────────

    [When(@"I tap the event ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task WhenITapTheEventInTheColumnFor(string eventText, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        await _dashboardPage.TapAgendaEventAsync(eventText, dateKey, calId);
    }

    [When(@"I tap the empty cell in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task WhenITapTheEmptyCellInTheColumnFor(string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        await _dashboardPage.TapAgendaCellAsync(dateKey, calId);
    }

    [When(@"I tap the overflow indicator for ""([^""]*)"" in ""([^""]*)""")]
    public async Task WhenITapTheOverflowIndicatorFor(string dateKey, string calendarName)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        await _dashboardPage.TapAgendaOverflowAsync(dateKey, calId);
    }

    [When(@"I tap the agenda create button")]
    public async Task WhenITapTheAgendaCreateButton()
    {
        await _dashboardPage.TapAgendaCreateButtonAsync();
    }

    [Then(@"I see the event modal")]
    public async Task ThenISeeTheEventModal()
    {
        await Assertions.Expect(_page.Locator(".modal-content")).ToBeVisibleAsync();
    }

    [Then(@"the modal start date contains ""([^""]*)""")]
    public async Task ThenTheModalStartDateContains(string dateStr)
    {
        var value = await _dashboardPage.GetModalStartDateValueAsync();
        value.Should().Contain(dateStr, $"The modal start datetime should contain '{dateStr}'.");
    }

    [Then(@"the modal start date contains today's date")]
    public async Task ThenTheModalStartDateContainsTodaysDate()
    {
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        var value = await _dashboardPage.GetModalStartDateValueAsync();
        value.Should().Contain(todayStr, "The modal start datetime should contain today's date.");
    }

    [Then(@"the ""([^""]*)"" chip is pre-selected")]
    public async Task ThenTheChipIsPreSelected(string calendarName)
    {
        var active = await _dashboardPage.IsCalendarChipActiveAsync(calendarName);
        active.Should().BeTrue($"The '{calendarName}' chip should be pre-selected in the modal.");
    }
}
