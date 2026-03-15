# E2E Testing Maintenance Guide

This document provides comprehensive guidance for maintaining and extending the FamilyHQ end-to-end (E2E) test suite.

## Table of Contents

1. [Running the E2E Tests](#running-the-e2e-tests)
2. [Test Structure](#test-structure)
3. [Adding New Tests](#adding-new-tests)
4. [Current Test Coverage](#current-test-coverage)
5. [Maintenance Checklist](#maintenance-checklist)

---

## Running the E2E Tests

### Prerequisites

Before running E2E tests, ensure the following are installed:

- **.NET 10 SDK** - Required to build and run the test projects
- **Playwright Browsers** - Chromium browser must be installed for Playwright

To install Playwright browsers, run:
```bash
cd tests-e2e/FamilyHQ.E2E.Common
dotnet playwright install chromium
```

### Required Services

The E2E tests require three services to be running simultaneously:

| Service | Port | Description |
|---------|------|-------------|
| WebApi | 5000/5001 | ASP.NET Core backend API |
| WebUi | 7154 | The Blazor WASM frontend application |
| Simulator | 7199 | Test data simulator API |

#### Starting the Services

1. **Start the WebApi**:
   ```bash
   cd src/FamilyHQ.WebApi
   dotnet run
   ```

2. **Start the Simulator API** (in a new terminal):
   ```bash
   cd tools/FamilyHQ.Simulator
   dotnet run
   ```

3. **Start the WebUI** (in a new terminal):
   ```bash
   cd src/FamilyHQ.WebUi
   dotnet run
   ```

### Running the Tests

Run all E2E tests from the Features project:

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test
```

To run tests with verbose output:

```bash
dotnet test --logger "console;verbosity=detailed"
```

To run a specific scenario:

```bash
dotnet test --filter "Scenario=View upcoming events on the dashboard month view"
```

---

## Test Structure

The E2E test suite follows a **4-project structure** that separates concerns and promotes maintainability:

```
tests-e2e/
├── FamilyHQ.E2E.Common/          # Shared utilities and page objects
├── FamilyHQ.E2E.Data/            # Test data and API clients
├── FamilyHQ.E2E.Steps/           # Step definitions (BDD glue code)
└── FamilyHQ.E2E.Features/        # Feature files (Gherkin scenarios)
```

### Project Responsibilities

#### FamilyHQ.E2E.Common
Contains shared infrastructure used across all test projects:

- **Pages/** - Page Object Model classes
- **Hooks/** - Playwright driver initialization
- **Configuration/** - Test configuration loading

Key files:
- [`Pages/BasePage.cs`](tests-e2e/FamilyHQ.E2E.Common/Pages/BasePage.cs) - Base class for all page objects
- [`Pages/DashboardPage.cs`](tests-e2e/FamilyHQ.E2E.Common/Pages/DashboardPage.cs) - Dashboard page object
- [`Hooks/PlaywrightDriver.cs`](tests-e2e/FamilyHQ.E2E.Common/Hooks/PlaywrightDriver.cs) - Browser initialization
- [`Configuration/TestConfiguration.cs`](tests-e2e/FamilyHQ.E2E.Common/Configuration/TestConfiguration.cs) - Test settings

#### FamilyHQ.E2E.Data
Handles test data management and API communication:

- **Templates/** - User template JSON files
- **Models/** - Data models for simulator API
- **Api/** - HTTP client for Simulator API

Key files:
- [`Templates/user_templates.json`](tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json) - User profile templates
- [`Api/SimulatorApiClient.cs`](tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs) - API client

#### FamilyHQ.E2E.Steps
Contains Reqnroll step definitions that connect Gherkin scenarios to code:

- **Steps/** - Step definition classes with [Binding] attributes
- **Hooks/** - Reqnroll hooks for setup/teardown

Key files:
- [`Steps/DashboardSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs) - Dashboard interaction steps
- [`Steps/EventSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs) - Event creation steps
- [`Steps/UserSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs) - User setup steps

#### FamilyHQ.E2E.Features
Contains Gherkin feature files written in natural language:

- **Dashboard.feature** - Main dashboard functionality scenarios

Key files:
- [`Dashboard.feature`](tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature) - All dashboard test scenarios

### Page Object Model Pattern

The tests use the **Page Object Model (POM)** pattern to encapsulate UI interactions:

```csharp
public class DashboardPage : BasePage
{
    private ILocator AddEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Add Event" });
    
    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        // ... modal interaction
    }
}
```

Benefits:
- **Encapsulation** - UI details are hidden behind method calls
- **Maintainability** - UI changes only require updates in one place
- **Reusability** - Page methods can be used across multiple scenarios

### BDD/Reqnroll Framework

The tests use **Reqnroll** (a BDD framework compatible with SpecFlow) to write tests in natural language:

- **Feature files** - Human-readable scenarios in Gherkin syntax
- **Step definitions** - C# methods that implement each Gherkin step
- **Bindings** - Connect steps to methods using `[Binding]` attributes

Example Gherkin:
```gherkin
Scenario: Create a new event
  Given I view the dashboard
  When I create an event "Dentist Appointment"
  Then I see the event "Dentist Appointment" displayed on the calendar
```

---

## Adding New Tests

### Adding New User Templates

User templates define the default calendars and event structure for test users. Edit [`user_templates.json`](tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json):

```json
{
    "New User Type": {
        "Calendars": [
            {
                "Id": "template_work",
                "Summary": "Work Calendar",
                "BackgroundColor": "#ea4335"
            }
        ],
        "Events": []
    }
}
```

Then use the template in a scenario:
```gherkin
Given I have a user like "New User Type" with calendar "Work Calendar"
```

### Adding New Feature Scenarios

Add new scenarios to an existing `.feature` file or create a new one:

```gherkin
Scenario: New scenario description
  Given I have a user like "Test Family Member" with calendar "Family Events"
  And the user has an all-day event "New Event" tomorrow
  When I view the dashboard
  Then I see the event "New Event" displayed on the calendar
```

### Adding New Step Definitions

When a new step doesn't match existing step definitions, add a new method in the appropriate Steps class:

```csharp
[Given(@"the user has a recurring event ""([^""]*)"" every Monday")]
public async Task GivenTheUserHasARecurringEventEveryMonday(string eventName)
{
    // Implementation here
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    // ... create recurring event
    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}
```

### Extending Page Objects

Add new methods to existing page objects or create new page object classes:

```csharp
// In DashboardPage.cs or a new page class
public async Task FilterByCalendarAsync(string calendarName)
{
    var filterDropdown = Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by calendar" });
    await filterDropdown.SelectOptionAsync(new[] { calendarName });
}
```

---

## Current Test Coverage

### Dashboard.feature Scenarios

The [`Dashboard.feature`](tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature) file contains **8 scenarios** covering the following functionality:

| Scenario | Category | Description |
|----------|----------|-------------|
| View upcoming events on the dashboard month view | **Display** | Verifies events appear in the month calendar view |
| Create a new event | **CRUD** | Tests event creation through the UI |
| Update an existing event | **CRUD** | Tests renaming an event |
| Delete an existing event | **CRUD** | Tests event deletion |
| View events from multiple calendars | **Multi-Calendar** | Verifies events from 3 different calendars display together |
| View all-day events | **Event Types** | Tests all-day event display |
| View timed events | **Event Types** | Tests timed event display with duration |
| View event details | **Interaction** | Tests clicking an event shows details modal |
| Navigate to next month | **Navigation** | Tests month navigation functionality |

### Test Categories

1. **Display Tests** - Verify events render correctly on the calendar
2. **CRUD Tests** - Create, Read, Update, Delete operations
3. **Multi-Calendar Tests** - Events from multiple calendar sources
4. **Event Types Tests** - All-day vs timed events
5. **Interaction Tests** - User interactions like clicking, navigating

---

## Maintenance Checklist

### When to Update Tests

Update E2E tests in these scenarios:

- **UI Changes** - When the dashboard UI is modified (buttons moved, classes changed)
- **New Features** - When new functionality is added to the dashboard
- **Bug Fixes** - When a bug is fixed, add a regression test
- **API Changes** - When the Simulator API contract changes

### Handling Test Failures

1. **Analyze the Failure**
   - Check if it's a genuine regression or a flaky test
   - Review Playwright's detailed error messages
   - Look at screenshots captured on failure (if configured)

2. **Common Issues**
   - **Timeout errors** - Increase timeout in `TestConfiguration.cs`
   - **Locator not found** - UI may have changed; update locators
   - **Service unavailable** - Ensure WebApi, WebUI, and Simulator are running

3. **Fix the Test**
   - Update locators if UI changed
   - Add waits if timing is an issue
   - Update step definitions for new functionality

### Debugging Tips

1. **Run in Headed Mode**
   ```bash
   # Set Headless = false in TestConfiguration or via environment variable
   ```

2. **Use Playwright Inspector**
   ```bash
   cd tests-e2e/FamilyHQ.E2E.Common
   dotnet playwright codegen
   ```

3. **Add Debug Output**
   ```csharp
   // Take screenshot on failure
   await Page.ScreenshotAsync(new() { Path = "failure.png" });
   ```

4. **Isolate Failing Tests**
   ```bash
   dotnet test --filter "FullyQualifiedName~DashboardSteps"
   ```

5. **Check Logs**
   - Browser console logs in Playwright
   - WebUI application logs
   - Simulator API logs

### Best Practices

- **Keep tests independent** - Each scenario should work in isolation
- **Use meaningful names** - Step definitions should clearly describe actions
- **Avoid hardcoded waits** - Prefer explicit waits for elements
- **Maintain the page object pattern** - Don't expose Playwright locators in step definitions
- **Keep scenarios focused** - One scenario per behavior being tested
- **Update templates carefully** - User templates affect multiple scenarios
