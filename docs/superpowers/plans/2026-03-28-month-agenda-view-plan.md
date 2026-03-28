# Month Agenda View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Month Agenda view to the FamilyHQ dashboard — a full-month table with one column per calendar, all days visible without scrolling, supporting create/edit/overflow interactions and live SignalR refresh.

**Architecture:** Inline third `@if` block in `Index.razor` (Option A). New `AgendaSteps.cs` step definitions file; agenda-specific methods added to `DashboardPage.cs`. All tests are E2E — no unit tests.

**Tech Stack:** Blazor WASM (.NET 10), Bootstrap CSS, Reqnroll/Playwright E2E, C#

---

## Files Changed

| Action | File |
|--------|------|
| Modify | `src/FamilyHQ.WebUi/Pages/Index.razor` |
| Modify | `src/FamilyHQ.WebUi/wwwroot/css/app.css` |
| Modify | `tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs` |
| Create | `tests-e2e/FamilyHQ.E2E.Features/WebUi/DashboardCalendarViewer/MonthAgendaView.feature` |
| Create | `tests-e2e/FamilyHQ.E2E.Steps/AgendaSteps.cs` |
| Modify | `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs` |
| Modify | `tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json` |

---

### Task 1: Create feature branch

- [ ] **Step 1: Branch from dev**

```bash
cd D:\Git\Echoes675\FamilyHQ
git checkout dev
git pull
git checkout -b feature/month-agenda-view
```

Expected: `Switched to a new branch 'feature/month-agenda-view'`

---

### Task 2: Add missing user templates

The scenarios reference `StandardUser`, `OverflowUser`, and `SyncUser` which do not exist in `user_templates.json`. `MultiCalUser` already exists — do NOT add it again.

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json`

- [ ] **Step 1: Verify `StandardUser`, `OverflowUser`, and `SyncUser` are absent, then add them at the end of the JSON object (before the closing `}`)**

```json
  ,
  "StandardUser": {
    "Calendars": [
      {
        "Id": "template_standard_work",
        "Summary": "Work Calendar",
        "BackgroundColor": "#ea4335"
      }
    ],
    "Events": []
  },
  "OverflowUser": {
    "Calendars": [
      {
        "Id": "template_overflow_work",
        "Summary": "Work Calendar",
        "BackgroundColor": "#ea4335"
      }
    ],
    "Events": []
  },
  "SyncUser": {
    "Calendars": [
      {
        "Id": "template_sync_work",
        "Summary": "Work Calendar",
        "BackgroundColor": "#ea4335"
      }
    ],
    "Events": []
  }
```

- [ ] **Step 2: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json
git commit -m "test: add StandardUser, OverflowUser, SyncUser templates"
```

---

### Task 3: Update DashboardPage.cs

Add data-testid locators for tabs, update `WaitForCalendarVisibleAsync`, add all agenda-specific page object methods. All new methods follow existing patterns in the file.

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs`

- [ ] **Step 1: Update tab locators and add AgendaViewContainer**

Replace lines 18–21 (the locator block):
```csharp
public ILocator MonthTable => Page.Locator("table.month-table");
public ILocator DayViewContainer => Page.Locator(".day-view-container");
public ILocator MonthTab => Page.GetByRole(AriaRole.Button, new() { Name = "Month View" });
public ILocator DayTab => Page.GetByRole(AriaRole.Button, new() { Name = "Day View" });
```
With:
```csharp
public ILocator MonthTable => Page.Locator("table.month-table");
public ILocator DayViewContainer => Page.Locator(".day-view-container");
public ILocator AgendaViewContainer => Page.Locator(".agenda-view-container");
public ILocator MonthTab => Page.GetByTestId("month-tab");
public ILocator DayTab => Page.GetByTestId("day-tab");
public ILocator AgendaTab => Page.GetByTestId("agenda-tab");
```

- [ ] **Step 2: Update WaitForCalendarVisibleAsync (line 66)**

Replace:
```csharp
await Page.Locator(".month-table, .day-view-container").First.WaitForAsync(
```
With:
```csharp
await Page.Locator(".month-table, .day-view-container, .agenda-view-container").First.WaitForAsync(
```

- [ ] **Step 3: Add SwitchToAgendaViewAsync after SwitchToMonthViewAsync (after line 80)**

```csharp
public async Task SwitchToAgendaViewAsync()
{
    await AgendaTab.ClickAsync();
    await AgendaViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
}
```

- [ ] **Step 4: Add agenda navigation methods after SwitchToAgendaViewAsync**

```csharp
public async Task NavigateAgendaPrevMonthAsync()
{
    var current = await GetAgendaCurrentMonthAsync();
    var expectedText = current.AddMonths(-1).ToString("MMMM yyyy");
    await Page.GetByTestId("agenda-prev-month").ClickAsync();
    await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
        .ToHaveTextAsync(expectedText, new() { Timeout = 10000 });
    await Page.WaitForTimeoutAsync(1000);
}

public async Task NavigateAgendaNextMonthAsync()
{
    var current = await GetAgendaCurrentMonthAsync();
    var expectedText = current.AddMonths(1).ToString("MMMM yyyy");
    await Page.GetByTestId("agenda-next-month").ClickAsync();
    await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
        .ToHaveTextAsync(expectedText, new() { Timeout = 10000 });
    await Page.WaitForTimeoutAsync(1000);
}

public async Task<string> GetAgendaMonthYearTextAsync()
{
    return (await Page.GetByTestId("agenda-month-year-label").InnerTextAsync()).Trim();
}

private async Task<DateTime> GetAgendaCurrentMonthAsync()
{
    var text = await GetAgendaMonthYearTextAsync();
    return DateTime.ParseExact(text, "MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Add agenda display assertion methods**

```csharp
public async Task<int> GetAgendaDayRowCountAsync()
{
    return await Page.Locator(".agenda-day-row").CountAsync();
}

public async Task<bool> HasTodayRowHighlightAsync()
{
    return await Page.Locator(".agenda-today-row").CountAsync() == 1;
}

public async Task<bool> WeekendRowsHaveClassAsync()
{
    // A month always has at least 8 weekend days
    return await Page.Locator(".agenda-weekend-row").CountAsync() >= 8;
}

public async Task<int> GetWeekdayRowsWithoutWeekendClassAsync()
{
    // All .agenda-day-row rows that do NOT have .agenda-weekend-row
    var all = await Page.Locator(".agenda-day-row").CountAsync();
    var weekends = await Page.Locator(".agenda-weekend-row").CountAsync();
    return all - weekends;
}

public async Task<bool> IsAgendaCalendarHeaderVisibleAsync(string calendarName)
{
    var header = Page.Locator("[data-testid^='agenda-calendar-header-']")
                     .Filter(new() { HasText = calendarName });
    return await header.CountAsync() > 0;
}
```

- [ ] **Step 6: Add agenda cell/event methods**

```csharp
public async Task<bool> IsAgendaEventVisibleAsync(string expectedText, string dateKey, Guid calendarId)
{
    var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
    return await cell.GetByText(expectedText, new() { Exact = false }).CountAsync() > 0;
}

public async Task<int> GetAgendaEventLineCountAsync(string dateKey, Guid calendarId)
{
    var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
    return await cell.Locator(".agenda-event-line").CountAsync();
}

public async Task<string> GetAgendaOverflowTextAsync(string dateKey, Guid calendarId)
{
    return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").InnerTextAsync();
}

public async Task<bool> IsAgendaOverflowVisibleAsync(string dateKey, Guid calendarId)
{
    return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").CountAsync() > 0;
}
```

- [ ] **Step 7: Add agenda interaction methods**

```csharp
public async Task TapAgendaEventAsync(string eventText, string dateKey, Guid calendarId)
{
    var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
    await cell.GetByText(eventText, new() { Exact = false }).First.ClickAsync();
    await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
}

public async Task TapAgendaCellAsync(string dateKey, Guid calendarId)
{
    // Click the cell itself (not an event line) to trigger the create modal
    await Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}").ClickAsync(
        new() { Position = new Position { X = 5, Y = 5 } });
    await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
}

public async Task TapAgendaOverflowAsync(string dateKey, Guid calendarId)
{
    await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").ClickAsync();
    await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
}

public async Task TapAgendaCreateButtonAsync()
{
    await Page.GetByTestId("agenda-create-button").ClickAsync();
    await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
}

public async Task<string> GetModalStartDateValueAsync()
{
    // Timed events use input[type='datetime-local'], value format: "yyyy-MM-ddTHH:mm"
    var input = EventModal.Locator("input[type='datetime-local']");
    return await input.InputValueAsync();
}

public async Task<bool> IsCalendarChipActiveAsync(string calendarName)
{
    var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
    var classes = await chip.GetAttributeAsync("class") ?? "";
    return classes.Contains("chip-active");
}
```

- [ ] **Step 8: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs
git commit -m "test: update DashboardPage for month agenda view"
```

---

### Task 4: Write MonthAgendaView.feature

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Features/WebUi/DashboardCalendarViewer/MonthAgendaView.feature`

- [ ] **Step 1: Create the feature file**

```gherkin
Feature: Month Agenda View
  As a family member
  I want to view my calendar in a month agenda layout
  So that I can see all days of the current month at a glance with events per calendar column

  # Navigation Scenarios

  Scenario: Agenda tab is visible and navigates to the agenda view
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see the month agenda view

  Scenario: Navigating to the previous month updates the displayed days
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate to the previous month on the agenda view
    Then the agenda view shows the previous month

  Scenario: Navigating to the next month updates the displayed days
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate to the next month on the agenda view
    Then the agenda view shows the next month

  Scenario: Switching from Agenda view to Day view preserves context
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I click the "Day View" tab
    Then I see the Day View Container

  Scenario: Tapping "+N more" navigates to the day view for the correct date
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "2026-06-15" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the overflow indicator for "2026-06-15" in "Work Calendar"
    Then I see the Day View Container

  # Display Scenarios

  Scenario: All days of the current month are visible
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then the agenda view shows all days of the current month

  Scenario: Weekend rows have a different background shade
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then weekend rows on the agenda view have the CSS class "agenda-weekend-row"
    And weekday rows on the agenda view do not have the class "agenda-weekend-row"

  Scenario: Today's row is highlighted
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then today's row on the agenda view has the CSS class "agenda-today-row"

  Scenario: Calendar column headers display correct names
    Given I have a user like "MultiCalUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "MultiCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    Then I see a column header for "Work Calendar"
    And I see a column header for "Personal Calendar"

  Scenario: Timed events display in 24hr HH:mm format
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "14:30" on "2026-06-15" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "14:30 Standup" in the "Work Calendar" column for "2026-06-15"

  Scenario: All-day events display title only with no time prefix
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Bank Holiday" on "2026-06-17" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "Bank Holiday" in the "Work Calendar" column for "2026-06-17"
    And the event "Bank Holiday" has no time prefix in the "Work Calendar" column for "2026-06-17"

  Scenario: A day with more than 3 events shows 3 lines and a "+N more" indicator
    Given I have a user like "OverflowUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has 4 events on "2026-06-15" in "Work Calendar"
    And I login as the user "OverflowUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see 3 event lines in the "Work Calendar" column for "2026-06-15"
    And I see a "+1 more" indicator in the "Work Calendar" column for "2026-06-15"

  Scenario: An event in two calendars appears in both columns
    Given I have a user like "MultiCalUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has an all-day event "Team Meeting" on "2026-06-20" in "Work Calendar"
    And the user has the event "Team Meeting" also in "Personal Calendar"
    And I login as the user "MultiCalUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    Then I see the event "Team Meeting" in the "Work Calendar" column for "2026-06-20"
    And I see the event "Team Meeting" in the "Personal Calendar" column for "2026-06-20"

  # Interaction Scenarios

  Scenario: Tapping an event opens the edit modal
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "2026-06-15" in "Work Calendar"
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the event "09:00 Standup" in the "Work Calendar" column for "2026-06-15"
    Then I see the event modal
    And I see the event details for "Standup"

  Scenario: Tapping an empty cell opens the create modal with correct date and calendar
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the empty cell in the "Work Calendar" column for "2026-06-25"
    Then I see the event modal
    And the modal start date contains "2026-06-25"
    And the "Work Calendar" chip is pre-selected

  Scenario: Tapping the create button opens the modal with today's date
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I tap the agenda create button
    Then I see the event modal
    And the modal start date contains today's date

  Scenario: Creating an event via the modal refreshes the agenda view
    Given I have a user like "StandardUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "StandardUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And I tap the empty cell in the "Work Calendar" column for "2026-06-20"
    And I fill in and save the event "New Meeting"
    Then I see the event "New Meeting" in the "Work Calendar" column for "2026-06-20"

  # Sync Scenarios

  Scenario: A newly synced event appears in the correct calendar column
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And a new event "Synced Meeting" is added to Google Calendar on "2026-06-18" in "Work Calendar"
    And Google Calendar sends a webhook notification
    Then I see the event "Synced Meeting" in the "Work Calendar" column for "2026-06-18"

  Scenario: A synced event deletion removes the event from the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Standup" at "09:00" on "2026-06-18" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And the event "Standup" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then I do not see "Standup" in the "Work Calendar" column for "2026-06-18"

  Scenario: A synced event update is reflected in the agenda view
    Given I have a user like "SyncUser"
    And the "Work Calendar" calendar is the active calendar
    And the user has a timed event "Old Title" at "09:00" on "2026-06-18" in "Work Calendar"
    And I login as the user "SyncUser"
    When I view the dashboard
    And I click the "Agenda" tab
    And I navigate the agenda to "June 2026"
    And the event "Old Title" is updated to "New Title" in Google Calendar
    And Google Calendar sends a webhook notification
    Then I see the event "New Title" in the "Work Calendar" column for "2026-06-18"
    And I do not see "Old Title" in the "Work Calendar" column for "2026-06-18"
```

- [ ] **Step 2: Commit**

```bash
git add "tests-e2e/FamilyHQ.E2E.Features/WebUi/DashboardCalendarViewer/MonthAgendaView.feature"
git commit -m "test: add MonthAgendaView.feature with 18 scenarios"
```

---

### Task 5: Write AgendaSteps.cs and extend EventSteps.cs

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Steps/AgendaSteps.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs`

- [ ] **Step 1: Create AgendaSteps.cs**

```csharp
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

    public AgendaSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    private Guid GetCalendarIdByName(string calendarName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var cal = template.Calendars.Find(c => c.Summary == calendarName)
                  ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found in template.");
        // The template IDs are re-mapped to new GUIDs per scenario in UserSteps.
        // We need the CalendarInfo.Id from the running app — but since we can only
        // match by name in page selectors, we use testid partial matching.
        // Resolved: use page locator by partial testid attribute + calendar name text.
        throw new InvalidOperationException(
            "Use ResolveCalendarIdFromPageAsync to get the live calendar ID.");
    }

    /// <summary>
    /// Resolves the live calendar GUID by finding the column header whose text matches
    /// calendarName and extracting the GUID from its data-testid attribute.
    /// </summary>
    private async Task<Guid> ResolveCalendarIdFromPageAsync(string calendarName)
    {
        var header = _dashboardPage.Page.Locator("[data-testid^='agenda-calendar-header-']")
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
            _dashboardPage.Page.GetByTestId($"agenda-cell-{dateKey}-{calId}")
                               .GetByText(expectedText, new() { Exact = false })
                               .First)
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Then(@"I do not see ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenIDoNotSeeInTheColumnFor(string text, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        var visible = await _dashboardPage.IsAgendaEventVisibleAsync(text, dateKey, calId);
        visible.Should().BeFalse($"'{text}' should not be visible in {calendarName} on {dateKey}.");
    }

    [Then(@"the event ""([^""]*)"" has no time prefix in the ""([^""]*)"" column for ""([^""]*)""")]
    public async Task ThenTheEventHasNoTimePrefixInTheColumnFor(string title, string calendarName, string dateKey)
    {
        var calId = await ResolveCalendarIdFromPageAsync(calendarName);
        var cell = _dashboardPage.Page.GetByTestId($"agenda-cell-{dateKey}-{calId}");
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
        await Assertions.Expect(_dashboardPage.Page.Locator(".modal-content")).ToBeVisibleAsync();
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

    // ─── Sync ────────────────────────────────────────────────────────────────────

    [Then(@"I see the event ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    // Note: This step is identical to the display step above — Reqnroll will reuse it.
    // No duplicate implementation needed.

    [Then(@"I do not see ""([^""]*)"" in the ""([^""]*)"" column for ""([^""]*)""")]
    // Same — reused from display section.
}
```

**Note:** The `Then I see the event` and `Then I do not see` steps are already defined above and will be reused by the sync scenarios. Remove the duplicate declarations shown at the bottom of the class — they are included here only for clarity.

- [ ] **Step 2: Add absolute-date event setup steps to EventSteps.cs**

Add the following methods to `EventSteps.cs`:

```csharp
[Given(@"the user has a timed event ""([^""]*)"" at ""([^""]*)"" on ""([^""]*)"" in ""([^""]*)""")]
public async Task GivenTheUserHasATimedEventAtOnInCalendar(
    string eventName, string timeStr, string dateStr, string calendarName)
{
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                   ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var timeParts = timeStr.Split(':');
    var startTime = date.AddHours(int.Parse(timeParts[0])).AddMinutes(int.Parse(timeParts[1]));

    isolatedTemplate.Events.Add(new SimulatorEventModel
    {
        Id = "evt_" + Guid.NewGuid().ToString("N"),
        CalendarId = calendar.Id,
        Summary = eventName,
        StartTime = startTime,
        EndTime = startTime.AddHours(1),
        IsAllDay = false
    });

    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}

[Given(@"the user has an all-day event ""([^""]*)"" on ""([^""]*)"" in ""([^""]*)""")]
public async Task GivenTheUserHasAnAllDayEventOnInCalendar(
    string eventName, string dateStr, string calendarName)
{
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                   ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    isolatedTemplate.Events.Add(new SimulatorEventModel
    {
        Id = "evt_" + Guid.NewGuid().ToString("N"),
        CalendarId = calendar.Id,
        Summary = eventName,
        StartTime = date,
        EndTime = date.AddDays(1),
        IsAllDay = true
    });

    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}

[Given(@"the user has (\d+) events on ""([^""]*)"" in ""([^""]*)""")]
public async Task GivenTheUserHasNEventsOnInCalendar(int count, string dateStr, string calendarName)
{
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                   ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    for (int i = 0; i < count; i++)
    {
        var startTime = date.AddHours(8 + i);
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = $"Event {i + 1}",
            StartTime = startTime,
            EndTime = startTime.AddHours(1),
            IsAllDay = false
        });
    }

    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}
```

Also add the sync step that adds an event on a specific date to a specific calendar (the existing `WhenANewEventIsAddedToGoogleCalendar` only uses the active calendar and always creates an all-day event for "tomorrow"). Add this new overload to `WebhookDataSteps.cs`:

```csharp
[When(@"a new event ""([^""]*)"" is added to Google Calendar on ""([^""]*)"" in ""([^""]*)""")]
public async Task WhenANewEventIsAddedOnDateInCalendar(string eventName, string dateStr, string calendarName)
{
    var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var calendar = template.Calendars.Find(c => c.Summary == calendarName)
                   ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

    var date = DateTime.ParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    var eventId = await _simulatorApi.AddEventAsync(
        userId: template.UserName,
        calendarId: calendar.Id,
        summary: eventName,
        start: date,
        end: date.AddDays(1),
        isAllDay: true);

    eventId = eventId.Trim('"');
    _scenarioContext[$"CreatedEventId:{eventName}"] = eventId;
}
```

- [ ] **Step 3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/AgendaSteps.cs
git add tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs
git add tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs
git commit -m "test: add AgendaSteps.cs and absolute-date event setup steps"
```

---

### Task 6: Verify E2E tests fail as expected

> **Requires approval per AGENTS.md** — run the full E2E test suite. Confirm all prerequisites are running (WebApi :5000, WebUi :7154, Simulator :7199).

- [ ] **Step 1: Run E2E tests**

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test
```

Expected outcome: Build succeeds. The 18 new scenarios fail with step binding errors or because `.agenda-view-container` doesn't exist yet. Existing 27 scenarios pass (except `MonthTab`/`DayTab` testid locators may fail — this is expected and will be fixed in Task 7).

---

### Task 7: Add data-testids to existing tabs and Agenda tab in Index.razor

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor` lines 41–48

- [ ] **Step 1: Read lines 41–48 to confirm current markup**

The current tab markup is:
```html
<ul class="nav nav-tabs mb-4">
    <li class="nav-item">
        <button class="nav-link @(_currentView == DashboardView.Month ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Month)">Month View</button>
    </li>
    <li class="nav-item">
        <button class="nav-link @(_currentView == DashboardView.Day ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Day)">Day View</button>
    </li>
</ul>
```

- [ ] **Step 2: Replace the tab bar with three tabs plus data-testids**

```html
<ul class="nav nav-tabs mb-4">
    <li class="nav-item">
        <button data-testid="month-tab" class="nav-link @(_currentView == DashboardView.Month ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Month)">Month View</button>
    </li>
    <li class="nav-item">
        <button data-testid="agenda-tab" class="nav-link @(_currentView == DashboardView.MonthAgenda ? "active" : "")" @onclick="() => SwitchToView(DashboardView.MonthAgenda)">Agenda</button>
    </li>
    <li class="nav-item">
        <button data-testid="day-tab" class="nav-link @(_currentView == DashboardView.Day ? "active" : "")" @onclick="() => SwitchToView(DashboardView.Day)">Day View</button>
    </li>
</ul>
```

Note: `DashboardView.MonthAgenda` does not compile yet — that is added in Task 8. The build will fail until Task 8 is complete. Proceed directly to Task 8.

- [ ] **Step 3: Do NOT commit yet — continue to Task 8**

---

### Task 8: Add MonthAgenda enum value and navigation header @if block

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor` — `@code` section (line 425) and navigation header section (after line 66)

- [ ] **Step 1: Add MonthAgenda to the enum (line 425)**

Replace:
```csharp
private enum DashboardView { Month, Day }
```
With:
```csharp
private enum DashboardView { Month, MonthAgenda, Day }
```

- [ ] **Step 2: Add Agenda navigation header block after the Month nav block (after line 66)**

The current structure at lines 50–82:
```razor
@if (_currentView == DashboardView.Month)
{
    <div class="d-flex ...">  <!-- Month nav header -->
    ...
    </div>
}
else if (_currentView == DashboardView.Day)
{
    <div class="d-flex ...">  <!-- Day nav header -->
    ...
    </div>
}
```

Insert a new `else if` block between the Month and Day blocks:
```razor
else if (_currentView == DashboardView.MonthAgenda)
{
    <div class="d-flex justify-content-between align-items-center mb-4">
        <div class="btn-group">
            <button class="btn btn-outline-primary" data-testid="agenda-prev-month" @onclick="GoToPreviousMonth">&lt; Prev</button>
            <button class="btn btn-light" style="min-width: 150px; font-weight: 600;" data-testid="agenda-month-year-label" @onclick="OpenQuickJumpModal">
                @CurrentMonth.ToString("MMMM yyyy")
            </button>
            <button class="btn btn-outline-primary" data-testid="agenda-next-month" @onclick="GoToNextMonth">Next &gt;</button>
        </div>
        <button class="btn btn-primary ms-3" data-testid="agenda-create-button" @onclick="() => OpenAddEventModal(DateTime.Today)">
            <i class="bi bi-plus-circle"></i> Add Event
        </button>
    </div>
}
```

**Before inserting, verify the exact method names** used by the Month view's prev/next buttons by reading lines 54–60. The spec uses `GoToPreviousMonth` / `GoToNextMonth` — confirm these match the actual method names in the `@code` block.

- [ ] **Step 3: Verify the file builds**

```bash
cd D:\Git\Echoes675\FamilyHQ
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat: add MonthAgenda enum value and tab navigation"
```

---

### Task 9: Add the agenda table @if block inside the null-check guard

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor` — inside the `else` block at line 102

- [ ] **Step 1: Read lines 102–112 to locate the insertion point**

The current structure:
```razor
else    // _monthView != null, not loading
{
    @if (_currentView == DashboardView.Month)
    {
        <table class="month-table">...
    }
    else if (_currentView == DashboardView.Day)
    {
        <div class="day-view-container">...
    }
}
```

- [ ] **Step 2: Add the agenda @else if block at the end of this else block (after the Day view closing `}`)**

```razor
else if (_currentView == DashboardView.MonthAgenda)
{
    <div class="agenda-view-container">
        <table class="agenda-table">
            <thead>
                <tr class="agenda-header-row">
                    <th class="agenda-day-col">Day</th>
                    @foreach (var cal in _calendars)
                    {
                        <th data-testid="@($"agenda-calendar-header-{cal.Id}")" class="agenda-cal-col">
                            <span class="cal-color-dot" style="background-color: @(cal.Color ?? "#9e9e9e");"></span>
                            @cal.DisplayName
                        </th>
                    }
                </tr>
            </thead>
            <tbody>
                @{
                    var daysInMonth = DateTime.DaysInMonth(CurrentMonth.Year, CurrentMonth.Month);
                }
                @for (int d = 1; d <= daysInMonth; d++)
                {
                    var date = new DateTime(CurrentMonth.Year, CurrentMonth.Month, d);
                    var dateKey = date.ToString("yyyy-MM-dd");
                    var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    var isToday = date.Date == DateTime.Today;
                    var rowCss = "agenda-day-row"
                        + (isWeekend ? " agenda-weekend-row" : "")
                        + (isToday ? " agenda-today-row" : "");

                    <tr class="@rowCss">
                        <td data-testid="@($"agenda-day-label-{dateKey}")" class="agenda-day-col">
                            @date.ToString("ddd dd")
                        </td>
                        @foreach (var cal in _calendars)
                        {
                            var dayEvents = (_monthView.Days.ContainsKey(dateKey)
                                    ? _monthView.Days[dateKey]
                                    : new List<CalendarEventViewModel>())
                                .Where(e => e.CalendarInfoId == cal.Id)
                                .OrderBy(e => (e.IsAllDay || e.Start.Date < date.Date) ? 0 : 1)
                                .ThenBy(e => e.Start)
                                .ToList();

                            <td data-testid="@($"agenda-cell-{dateKey}-{cal.Id}")"
                                class="agenda-cal-cell"
                                @onclick="() => OpenAddEventModal(date, cal.Id)">
                                @foreach (var evt in dayEvents.Take(3))
                                {
                                    var isContinuation = !evt.IsAllDay && evt.Start.Date < date.Date;
                                    var displayText = (evt.IsAllDay || isContinuation)
                                        ? evt.Title
                                        : $"{evt.Start.LocalDateTime:HH:mm} {evt.Title}";

                                    <div data-testid="@($"agenda-event-{evt.Id}-{cal.Id}")"
                                         class="agenda-event-line"
                                         @onclick="() => OpenEditEventModal(evt)"
                                         @onclick:stopPropagation="true">
                                        @displayText
                                    </div>
                                }
                                @if (dayEvents.Count > 3)
                                {
                                    var overflowDate = DateTime.ParseExact(
                                        dateKey, "yyyy-MM-dd",
                                        System.Globalization.CultureInfo.InvariantCulture);
                                    <div data-testid="@($"agenda-overflow-{dateKey}-{cal.Id}")"
                                         class="agenda-overflow-btn"
                                         @onclick="() => SwitchToView(DashboardView.Day, overflowDate)"
                                         @onclick:stopPropagation="true">
                                        +@(dayEvents.Count - 3) more
                                    </div>
                                }
                            </td>
                        }
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

- [ ] **Step 3: Verify the file builds**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat: implement month agenda view table"
```

---

### Task 10: Add CSS for the agenda view

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

- [ ] **Step 1: Append agenda styles at the end of app.css**

```css
/* ── Month Agenda View ─────────────────────────────────────────────────────── */

.agenda-view-container {
    background: var(--surface);
    border-radius: 8px;
    box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1), 0 2px 4px -1px rgba(0,0,0,0.06);
    overflow: hidden;
}

.agenda-table {
    width: 100%;
    border-collapse: collapse;
    table-layout: fixed;
}

.agenda-table th,
.agenda-table td {
    border: 1px solid var(--border);
    padding: 2px 4px;
    vertical-align: top;
    overflow: hidden;
}

.agenda-header-row th {
    background-color: var(--surface);
    font-weight: 600;
    font-size: 0.8rem;
    color: var(--text-muted);
    text-align: center;
}

.agenda-day-col {
    width: 80px;
    white-space: nowrap;
    font-size: 0.8rem;
    font-weight: 600;
    color: var(--text-main);
}

.agenda-cal-col {
    font-size: 0.75rem;
}

.agenda-cal-cell {
    cursor: pointer;
    height: calc((100vh - 150px) / 31);
    font-size: 0.75rem;
    overflow: hidden;
}

.agenda-cal-cell:hover {
    background-color: rgba(0,0,0,0.02);
}

.agenda-weekend-row .agenda-day-col,
.agenda-weekend-row .agenda-cal-cell {
    background-color: var(--weekend-bg);
}

.agenda-today-row .agenda-day-col {
    color: var(--primary);
    font-weight: 700;
}

.agenda-today-row .agenda-cal-cell {
    background-color: var(--today-bg);
}

.agenda-event-line {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    color: var(--text-main);
    font-size: 0.75rem;
    padding: 1px 2px;
    cursor: pointer;
    border-radius: 2px;
}

.agenda-event-line:hover {
    background-color: rgba(0,0,0,0.07);
}

.agenda-overflow-btn {
    color: var(--text-muted);
    font-size: 0.7rem;
    cursor: pointer;
    padding: 1px 2px;
}

.agenda-overflow-btn:hover {
    color: var(--text-main);
    text-decoration: underline;
}

.cal-color-dot {
    display: inline-block;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    margin-right: 4px;
    vertical-align: middle;
}
```

- [ ] **Step 2: Verify the project builds**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat: add agenda view CSS styles"
```

---

### Task 11: Run full E2E suite and verify all scenarios pass

> **Requires approval per AGENTS.md.** Ensure all three services are running before executing.

Start services (three separate terminals):
```bash
# Terminal 1
cd src/FamilyHQ.WebApi && dotnet run

# Terminal 2
cd tools/FamilyHQ.Simulator && dotnet run

# Terminal 3
cd src/FamilyHQ.WebUi && dotnet run
```

- [ ] **Step 1: Run all E2E tests**

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test --logger "console;verbosity=detailed"
```

Expected: All 45 scenarios pass (27 existing + 18 new). If any fail, investigate before proceeding.

- [ ] **Step 2: If existing tests now fail due to tab testid changes**

The `MonthTab`/`DayTab` locators in `DashboardPage.cs` were updated from `GetByRole` to `GetByTestId`. The underlying HTML now has `data-testid` attributes. If existing scenarios fail, verify that `data-testid="month-tab"` and `data-testid="day-tab"` are present on the rendered tab buttons.

To run only the existing month view scenarios to confirm they still pass:
```bash
dotnet test --filter "Feature=Month Calendar View"
```

To run only the new agenda scenarios:
```bash
dotnet test --filter "Feature=Month Agenda View"
```

---

### Task 12: Final commit and push

- [ ] **Step 1: Verify git status is clean**

```bash
git status
```

Expected: `nothing to commit, working tree clean`

- [ ] **Step 2: Push the branch**

```bash
git push -u origin feature/month-agenda-view
```
