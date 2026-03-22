# Webhook E2E Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 6 BDD E2E scenarios that verify Google Calendar webhook-driven sync updates the FamilyHQ dashboard — both via page reload (eventual consistency) and live SignalR push (real-time).

**Architecture:** A new `BackdoorEventsController` on the Simulator accepts direct event mutations (no OAuth token required). The `SimulatorApiClient` gains four methods to call these endpoints and trigger the existing webhook. A new `WebhookDataSteps.cs` wires up the Gherkin steps; a new `GoogleCalendarSync.feature` defines the 6 scenarios.

**Tech Stack:** .NET 10 · ASP.NET Core · Reqnroll (BDD) · Playwright · Entity Framework Core (Simulator) · SignalR

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| New | `tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs` | DTO for POST/PUT bodies to the backdoor controller |
| New | `tools/FamilyHQ.Simulator/Controllers/BackdoorEventsController.cs` | Direct event CRUD for test use — no OAuth token needed |
| Modify | `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs` | Add `AddEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`, `TriggerWebhookAsync` |
| New | `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs` | Gherkin step bindings for webhook data mutations and live-update assertions |
| New | `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature` | 6 BDD scenarios (3 eventual consistency, 3 real-time SignalR) |

---

## Task 1: Feature File (Failing Tests First)

Write the feature file before any implementation so we have a clear definition of done. Scenarios will fail with "step not bound" until later tasks complete — that is expected.

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature`

- [ ] **Step 1: Create the feature file**

```gherkin
Feature: Google Calendar Webhook Sync
  As a family member
  I want my dashboard to reflect changes made in Google Calendar
  So that I always see an up-to-date view of my schedule

  Scenario: New event added in Google Calendar appears on dashboard after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "Dentist Appointment" displayed on the calendar

  Scenario: Event updated in Google Calendar shows new title after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I see the event "School Holiday (Cancelled)" displayed on the calendar

  Scenario: Event deleted in Google Calendar disappears after sync
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    And I view the dashboard
    Then I do not see the event "School Holiday" displayed on the calendar

  Scenario: New event added in Google Calendar appears live on open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When a new event "Dentist Appointment" is added to Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "Dentist Appointment"

  Scenario: Event updated in Google Calendar shows live on open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is updated to "School Holiday (Cancelled)" in Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to show "School Holiday (Cancelled)"

  Scenario: Event deleted in Google Calendar disappears live from open dashboard
    Given I have a user like "TestFamilyMember" with calendar "Family Events"
    And the user has an all-day event "School Holiday" tomorrow
    And I login as the user "TestFamilyMember"
    And I view the dashboard
    When the event "School Holiday" is deleted from Google Calendar
    And Google Calendar sends a webhook notification
    Then the dashboard live-updates to remove "School Holiday"
```

- [ ] **Step 2: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature
git commit -m "test(e2e): add GoogleCalendarSync feature file"
```

---

## Task 2: BackdoorEventRequest DTO

**Files:**
- Create: `tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs`

Reference: `tools/FamilyHQ.Simulator/DTOs/GoogleEventRequest.cs` for the existing DTO pattern in this folder.

- [ ] **Step 1: Create the DTO**

```csharp
namespace FamilyHQ.Simulator.DTOs;

public class BackdoorEventRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? CalendarId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add tools/FamilyHQ.Simulator/DTOs/BackdoorEventRequest.cs
git commit -m "feat(simulator): add BackdoorEventRequest DTO"
```

---

## Task 3: BackdoorEventsController

Adds three endpoints to the Simulator that allow test code to directly add, update, or delete events without needing to construct an OAuth Bearer token.

**Files:**
- Create: `tools/FamilyHQ.Simulator/Controllers/BackdoorEventsController.cs`

Reference: `tools/FamilyHQ.Simulator/Controllers/SimulatorConfigController.cs` for the controller pattern (constructor, `_db`, `SaveChangesAsync`, `Ok()`).

> **Important:** Route is `api/simulator/backdoor/events` — under the `api/simulator/` namespace like `SimulatorConfigController`, NOT under `simulate/` like `WebhookController`.

- [ ] **Step 1: Create the controller**

```csharp
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("api/simulator/backdoor/events")]
public class BackdoorEventsController : ControllerBase
{
    private readonly SimContext _db;

    public BackdoorEventsController(SimContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Adds a new event directly to the simulator — bypasses OAuth, accepts userId in body.
    /// Returns the new event's ID.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddEvent([FromBody] BackdoorEventRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId) || string.IsNullOrWhiteSpace(body.CalendarId))
            return BadRequest("UserId and CalendarId are required.");

        var newEvent = new SimulatedEvent
        {
            Id = "simulated_evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = body.CalendarId,
            Summary = body.Summary,
            StartTime = body.Start,
            EndTime = body.End,
            IsAllDay = body.IsAllDay,
            UserId = body.UserId
        };

        _db.Events.Add(newEvent);
        await _db.SaveChangesAsync();

        return Ok(newEvent.Id);
    }

    /// <summary>
    /// Updates the summary of an existing event. Accepts userId in body.
    /// </summary>
    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEvent(string eventId, [FromBody] BackdoorEventRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId))
            return BadRequest("UserId is required.");

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == body.UserId);
        if (existing == null)
            return NotFound();

        existing.Summary = body.Summary;
        await _db.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    /// Deletes an event. Accepts userId as a query string parameter.
    /// </summary>
    [HttpDelete("{eventId}")]
    public async Task<IActionResult> DeleteEvent(string eventId, [FromQuery] string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest("userId query parameter is required.");

        var existing = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId && e.UserId == userId);
        if (existing == null)
            return NotFound();

        var attendees = _db.EventAttendees.Where(a => a.EventId == eventId);
        _db.EventAttendees.RemoveRange(attendees);
        _db.Events.Remove(existing);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
```

- [ ] **Step 2: Build the Simulator to verify it compiles**

```bash
cd tools/FamilyHQ.Simulator
dotnet build
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add tools/FamilyHQ.Simulator/Controllers/BackdoorEventsController.cs
git commit -m "feat(simulator): add BackdoorEventsController for test backdoor access"
```

---

## Task 4: SimulatorApiClient Extensions

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`

Read the full existing file before editing: `tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs`. It uses `PostAsJsonAsync` and `EnsureSuccessStatusCode()`. Follow those patterns exactly.

> **`TriggerWebhookAsync` note:** The relative URL must be `"simulate/push"` — no leading slash. A leading slash would discard the `BaseAddress` path and break routing.

- [ ] **Step 1: Add the four new methods to `SimulatorApiClient`**

Add these methods before `Dispose()`:

```csharp
/// <summary>
/// Adds a new event directly to the Simulator via the back-door endpoint.
/// Returns the newly created event's ID.
/// </summary>
public async Task<string> AddEventAsync(
    string userId, string calendarId, string summary,
    DateTime start, DateTime end, bool isAllDay)
{
    var body = new
    {
        UserId = userId,
        CalendarId = calendarId,
        Summary = summary,
        Start = start,
        End = end,
        IsAllDay = isAllDay
    };
    var response = await _httpClient.PostAsJsonAsync("api/simulator/backdoor/events", body);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

/// <summary>
/// Updates the summary of an existing event via the back-door endpoint.
/// </summary>
public async Task UpdateEventAsync(string userId, string eventId, string newSummary)
{
    var body = new { UserId = userId, Summary = newSummary };
    var response = await _httpClient.PutAsJsonAsync(
        $"api/simulator/backdoor/events/{eventId}", body);
    response.EnsureSuccessStatusCode();
}

/// <summary>
/// Deletes an event via the back-door endpoint.
/// </summary>
public async Task DeleteEventAsync(string userId, string eventId)
{
    var response = await _httpClient.DeleteAsync(
        $"api/simulator/backdoor/events/{eventId}?userId={userId}");
    response.EnsureSuccessStatusCode();
}

/// <summary>
/// Triggers the Simulator to fire a push notification to the WebApi webhook endpoint,
/// which causes the WebApi to run SyncAllAsync and notify clients via SignalR.
/// </summary>
public async Task TriggerWebhookAsync()
{
    // "simulate/push" — no leading slash, resolves against BaseAddress correctly
    var response = await _httpClient.PostAsync("simulate/push", null);
    response.EnsureSuccessStatusCode();
}
```

- [ ] **Step 2: Build the E2E.Data project**

```bash
cd tests-e2e/FamilyHQ.E2E.Data
dotnet build
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Data/Api/SimulatorApiClient.cs
git commit -m "feat(e2e): extend SimulatorApiClient with backdoor event methods and TriggerWebhookAsync"
```

---

## Task 5: WebhookDataSteps

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs`

Reference: `tests-e2e/FamilyHQ.E2E.Steps/DashboardSteps.cs` and `EventSteps.cs` for the `[Binding]`, constructor injection, and `ScenarioContext` patterns. `DashboardPage` is always constructed manually from `scenarioContext.Get<IPage>()` — it is not a DI-registered service.

**Key `ScenarioContext` keys (set by earlier steps):**
- `"UserTemplate"` → `SimulatorConfigurationModel` — has `.UserName` and `.Events` list
- `"CurrentCalendarId"` → `string` — the calendar ID for the current user's primary calendar
- `"LastCreatedEventId"` → `string` — written by the "added to Google Calendar" step

**Event ID resolution for update/delete steps:**
- If the event was just added via `AddEventAsync`, its ID is in `ScenarioContext["LastCreatedEventId"]`
- If the event was pre-seeded via `Given the user has an all-day event "X" tomorrow`, find its ID by searching `UserTemplate.Events` for the first entry where `Summary == eventName`

- [ ] **Step 1: Create `WebhookDataSteps.cs`**

```csharp
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WebhookDataSteps
{
    private const int LiveUpdateTimeoutMs = 5000;
    private const int LiveUpdatePollIntervalMs = 250;

    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly DashboardPage _dashboardPage;

    public WebhookDataSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _dashboardPage = new DashboardPage(scenarioContext.Get<IPage>());
    }

    [When(@"a new event ""([^""]*)"" is added to Google Calendar")]
    public async Task WhenANewEventIsAddedToGoogleCalendar(string eventName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");
        var tomorrow = DateTime.Today.AddDays(1);

        var eventId = await _simulatorApi.AddEventAsync(
            userId: template.UserName,
            calendarId: calendarId,
            summary: eventName,
            start: tomorrow,
            end: tomorrow.AddDays(1),
            isAllDay: true);

        // Strip surrounding quotes that ReadAsStringAsync may include
        eventId = eventId.Trim('"');
        _scenarioContext["LastCreatedEventId"] = eventId;
    }

    [When(@"the event ""([^""]*)"" is updated to ""([^""]*)"" in Google Calendar")]
    public async Task WhenTheEventIsUpdatedInGoogleCalendar(string originalName, string newName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var eventId = ResolveEventId(originalName);

        await _simulatorApi.UpdateEventAsync(template.UserName, eventId, newName);
    }

    [When(@"the event ""([^""]*)"" is deleted from Google Calendar")]
    public async Task WhenTheEventIsDeletedFromGoogleCalendar(string eventName)
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var eventId = ResolveEventId(eventName);

        await _simulatorApi.DeleteEventAsync(template.UserName, eventId);
    }

    [When(@"Google Calendar sends a webhook notification")]
    public async Task WhenGoogleCalendarSendsAWebhookNotification()
    {
        await _simulatorApi.TriggerWebhookAsync();
    }

    [Then(@"the dashboard live-updates to show ""([^""]*)""")]
    public async Task ThenTheDashboardLiveUpdatesToShow(string eventName)
    {
        await WaitForConditionAsync(
            condition: async () =>
            {
                var events = await _dashboardPage.GetVisibleEventsAsync();
                return events.Any(e => e.Contains(eventName));
            },
            failMessage: $"Dashboard did not live-update within 5s after webhook notification (expected to show '{eventName}')");
    }

    [Then(@"the dashboard live-updates to remove ""([^""]*)""")]
    public async Task ThenTheDashboardLiveUpdatesToRemove(string eventName)
    {
        await WaitForConditionAsync(
            condition: async () =>
            {
                var events = await _dashboardPage.GetVisibleEventsAsync();
                return !events.Any(e => e.Contains(eventName));
            },
            failMessage: $"Dashboard did not live-update within 5s after webhook notification (expected to remove '{eventName}')");
    }

    // Resolves an event ID from ScenarioContext.
    // Checks LastCreatedEventId first (for dynamically-added events), then falls back
    // to searching the UserTemplate's Events list by Summary (for pre-seeded events).
    private string ResolveEventId(string eventName)
    {
        if (_scenarioContext.TryGetValue("LastCreatedEventId", out var lastId) && lastId is string id)
            return id;

        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var match = template.Events.FirstOrDefault(e => e.Summary == eventName);
        if (match == null)
            throw new InvalidOperationException(
                $"Could not resolve event ID for '{eventName}'. " +
                "Ensure the event was seeded via 'Given the user has an all-day event' or 'When a new event is added'.");

        return match.Id;
    }

    private static async Task WaitForConditionAsync(Func<Task<bool>> condition, string failMessage)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(LiveUpdateTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(LiveUpdatePollIntervalMs);
        }
        throw new TimeoutException(failMessage);
    }
}
```

- [ ] **Step 2: Build the E2E.Steps project**

```bash
cd tests-e2e/FamilyHQ.E2E.Steps
dotnet build
```

Expected: Build succeeded, 0 error(s).

- [ ] **Step 3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs
git commit -m "test(e2e): add WebhookDataSteps with backdoor event mutation and live-update assertions"
```

---

## Task 6: Run the Tests

With all infrastructure in place, run the new scenarios end-to-end. The three services (WebApi, Simulator, WebUi) must be running first — see `.agent/docs/e2e-testing-maintenance.md` for startup instructions.

- [ ] **Step 1: Start the three services** (in separate terminals)

```bash
# Terminal 1
cd src/FamilyHQ.WebApi && dotnet run

# Terminal 2
cd tools/FamilyHQ.Simulator && dotnet run

# Terminal 3
cd src/FamilyHQ.WebUi && dotnet run
```

- [ ] **Step 2: Run only the new feature**

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test --filter "Feature=Google Calendar Webhook Sync" --logger "console;verbosity=detailed"
```

Expected: 6 scenarios pass.

- [ ] **Step 3: Run the full suite to verify no regressions**

```bash
dotnet test --logger "console;verbosity=detailed"
```

Expected: All scenarios pass (including all pre-existing `Dashboard.feature` scenarios).

- [ ] **Step 4: Commit if any test-fixing changes were needed**

If you had to fix any issues (locators, timing, etc.) during the run, commit those fixes now:

```bash
git add -p
git commit -m "fix(e2e): <describe what was fixed>"
```

---

## Definition of Done

- [ ] `GoogleCalendarSync.feature` exists with 6 scenarios
- [ ] All 6 new scenarios pass
- [ ] All pre-existing `Dashboard.feature` scenarios still pass
- [ ] `dotnet build` is clean for Simulator, E2E.Data, and E2E.Steps projects
