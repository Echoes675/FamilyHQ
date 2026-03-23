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
- [`DashboardSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs) - Dashboard navigation, event display assertions
- [`EventSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs) - Seeding events into the simulator before a scenario
- [`UserSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs) - User provisioning and login via OAuth flow
- [`AuthenticationSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/AuthenticationSteps.cs) - Sign-in / sign-out assertions
- [`WebhookDataSteps.cs`](tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs) - Backdoor event mutations and webhook trigger for sync scenarios
- [`Hooks/MasterHooks.cs`](tests-e2e/FamilyHQ.E2E.Steps/Hooks/MasterHooks.cs) - Per-scenario browser setup/teardown
- [`Hooks/TemplateHooks.cs`](tests-e2e/FamilyHQ.E2E.Steps/Hooks/TemplateHooks.cs) - Loads `user_templates.json` once before the test run

#### FamilyHQ.E2E.Features
Contains Gherkin feature files written in natural language:

Key files:
- [`Dashboard.feature`](tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature) - Dashboard calendar viewer scenarios (CRUD, multi-calendar, navigation)
- [`Authentication.feature`](tests-e2e/FamilyHQ.E2E.Features/Authentication.feature) - Sign-in and sign-out scenarios
- [`GoogleCalendarSync.feature`](tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature) - Webhook-triggered sync and live SignalR update scenarios

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

User templates define the calendars available to a test user. Each template key is used in the `Given I have a user like "..."` step. Edit [`user_templates.json`](tests-e2e/FamilyHQ.E2E.Data/Templates/user_templates.json):

```json
{
    "NewUserType": {
        "Calendars": [
            {
                "Id": "template_work",
                "Summary": "Work Calendar",
                "BackgroundColor": "#ea4335"
            },
            {
                "Id": "template_personal",
                "Summary": "Personal Calendar",
                "BackgroundColor": "#34a853"
            }
        ],
        "Events": []
    }
}
```

Each scenario gets a **unique isolated copy** of the template at runtime (unique username, new calendar IDs). Pre-seeded events are added via the `EventSteps` (`Given the user has an all-day event...`) rather than in the template's `Events` array.

Then use the template in a scenario:
```gherkin
Given I have a user like "NewUserType" with calendar "Work Calendar"
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

The suite currently contains **27 scenarios** across three feature files.

### Dashboard.feature (15 scenarios)

Has a `Background` that provisions a `TestFamilyMember` user and logs in before each scenario. Scenarios that need a different user provision their own user on top of the Background.

| Scenario | Category |
|----------|----------|
| View upcoming events on the dashboard month view | Display |
| Create a new event | CRUD |
| Update an existing event | CRUD |
| Delete an existing event | CRUD |
| View events from multiple calendars | Multi-Calendar |
| View all-day events | Event Types |
| View timed events | Event Types |
| View event details | Interaction |
| Update event after changing its calendar | Multi-Calendar / CRUD |
| Delete event after changing its calendar | Multi-Calendar / CRUD |
| Navigate to next month | Navigation |
| Create event in two calendars appears twice on grid | Multi-Calendar |
| Add calendar to existing event via chip | Multi-Calendar |
| Remove calendar chip from event | Multi-Calendar |
| Last chip is protected — cannot remove final calendar | Multi-Calendar |
| Delete event removes it from all calendars | Multi-Calendar / CRUD |

### Authentication.feature (5 scenarios)

| Scenario | Category |
|----------|----------|
| User sees sign-in button when not authenticated | Auth |
| User can sign in and see their username | Auth |
| User can sign out and return to sign-in screen | Auth |
| Calendar is hidden when not authenticated | Auth |
| Calendar is visible when authenticated | Auth |

### GoogleCalendarSync.feature (6 scenarios)

Tests the full webhook → sync → UI update pipeline using the Simulator's backdoor API to mutate events and the `/api/sync/webhook` endpoint to trigger a sync.

| Scenario | Category |
|----------|----------|
| New event added in Google Calendar appears on dashboard after sync | Webhook Sync |
| Event updated in Google Calendar shows new title after sync | Webhook Sync |
| Event deleted in Google Calendar disappears after sync | Webhook Sync |
| New event added in Google Calendar appears live on open dashboard | Live Update (SignalR) |
| Event updated in Google Calendar shows live on open dashboard | Live Update (SignalR) |
| Event deleted in Google Calendar disappears live from open dashboard | Live Update (SignalR) |

### Test Categories

1. **Display** - Events render correctly on the calendar grid
2. **CRUD** - Create, Update, Delete operations through the UI
3. **Multi-Calendar** - Chip selector, per-calendar capsule rendering, last-chip protection
4. **Event Types** - All-day vs timed event display
5. **Navigation** - Month navigation
6. **Auth** - Sign-in / sign-out flows
7. **Webhook Sync** - Events added/updated/deleted externally appear after a webhook sync
8. **Live Update** - SignalR pushes cause the open dashboard to refresh without navigation

---

## Tracing with Correlation IDs

The E2E test suite uses unique correlation IDs to facilitate debugging across multiple services:

### How it Works
1. **Scenario-scoped ID**: Every scenario generates a unique `TestCorrelationId` (GUID) via `CorrelationIdHooks.cs`.
2. **Browser Injection**: This ID is injected into the browser's `localStorage` as `familyhq_session_correlation_id`.
3. **Header Propagation**: The Blazor UI and Simulator API clients include this ID in the `X-Session-Correlation-Id` header for all outgoing requests.
4. **Log Stitching**: WebApi and Simulator services log this ID, allowing you to filter logs for a specific scenario across all services.

### Troubleshooting with ID
If a scenario fails, look for the `TestCorrelationId` in the test output. You can then use this ID to search through the logs of the WebApi, WebUi, and Simulator to trace the exact sequence of events that led to the failure.

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
