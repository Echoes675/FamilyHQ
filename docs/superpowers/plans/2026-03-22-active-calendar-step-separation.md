# Active Calendar Step Separation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the combined `Given I have a user like "X" with calendar "Y"` step into a user-setup step and a separate explicit calendar-activation step, with fail-fast error messages when the active calendar is not set.

**Architecture:** A new `ScenarioContextExtensions` class provides the fail-fast `GetCurrentCalendarId()` helper. `UserSteps` loses the `calendarName` parameter and gains a new activation step. All callers in `EventSteps` and `WebhookDataSteps` swap to the helper. Feature files are updated to use the two-step pattern only where needed.

**Tech Stack:** .NET 10, Reqnroll (BDD/Gherkin), xUnit, Playwright. Build via `dotnet build`. See `.agent/skills/bdd-testing/SKILL.md` and `.agent/skills/coding-standards/SKILL.md`.

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `tests-e2e/FamilyHQ.E2E.Steps/ScenarioContextExtensions.cs` | **Create** | `GetCurrentCalendarId()` extension with descriptive fail-fast |
| `tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs` | **Modify** | Remove `with calendar` param; add activation step |
| `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs` | **Modify** | Replace 3× raw `Get<string>("CurrentCalendarId")` with helper |
| `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs` | **Modify** | Replace 1× raw `Get<string>("CurrentCalendarId")` with helper |
| `tests-e2e/FamilyHQ.E2E.Features/Authentication.feature` | **Modify** | Split all `with calendar` steps; add activation where needed |
| `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature` | **Modify** | Split all `with calendar` steps; add activation where needed |
| `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature` | **Modify** | Split all `with calendar` steps; add activation to all scenarios |

---

## Task 1: Create the fail-fast helper

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Steps/ScenarioContextExtensions.cs`

- [ ] **Step 1.1: Create `ScenarioContextExtensions.cs`**

```csharp
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

public static class ScenarioContextExtensions
{
    public static string GetCurrentCalendarId(this ScenarioContext context)
    {
        if (!context.TryGetValue<string>("CurrentCalendarId", out var calendarId))
            throw new InvalidOperationException(
                "No active calendar has been selected. " +
                "Add 'And the \"<calendar name>\" calendar is the active calendar' to your scenario.");
        return calendarId;
    }
}
```

- [ ] **Step 1.2: Build the Steps project to confirm it compiles**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Steps/FamilyHQ.E2E.Steps.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 1.3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/ScenarioContextExtensions.cs
git commit -m "refactor(e2e): add ScenarioContextExtensions with fail-fast GetCurrentCalendarId helper"
```

---

## Task 2: Update callers to use the helper

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs` (lines 25, 46, 120)
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs` (line 30)

These files currently call `_scenarioContext.Get<string>("CurrentCalendarId")` directly, which throws a cryptic `KeyNotFoundException` when the key is absent. Replace all occurrences with the new helper.

- [ ] **Step 2.1: Update `EventSteps.cs` — three replacements**

In `GivenTheUserHasAnAll_DayEventTomorrow` (around line 25):
```csharp
// Before
var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");

// After
var calendarId = _scenarioContext.GetCurrentCalendarId();
```

Same replacement in `GivenTheUserHasAnAllDayEventInDays` (around line 46) and in `AddTimedEvent` (around line 120).

- [ ] **Step 2.2: Update `WebhookDataSteps.cs` — one replacement**

In `WhenANewEventIsAddedToGoogleCalendar` (line 30):
```csharp
// Before
var calendarId = _scenarioContext.Get<string>("CurrentCalendarId");

// After
var calendarId = _scenarioContext.GetCurrentCalendarId();
```

- [ ] **Step 2.3: Build to confirm no regressions**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Steps/FamilyHQ.E2E.Steps.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2.4: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs
git commit -m "refactor(e2e): replace raw CurrentCalendarId lookups with GetCurrentCalendarId helper"
```

---

## Task 3: Refactor `UserSteps.cs`

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs`

Replace the single combined step with two separate steps. The user-setup step loses the `calendarName` parameter entirely. A new activation step sets `CurrentCalendarId`.

- [ ] **Step 3.1: Replace `GivenIHaveAUserLikeWithCalendar` with `GivenIHaveAUserLike`**

Delete the existing `GivenIHaveAUserLikeWithCalendar` method and replace with:

```csharp
[Given(@"I have a user like ""([^""]*)""")]
public async Task GivenIHaveAUserLike(string userKey)
{
    if (!TemplateHooks.UserTemplates.TryGetValue(userKey, out var template))
        throw new Exception($"Template '{userKey}' not found in user_templates.json");

    var uniqueUsername = $"{userKey}_{Guid.NewGuid():N}";
    var isolatedTemplate = new SimulatorConfigurationModel { UserName = uniqueUsername };
    var newCalendarIds = new Dictionary<string, string>();

    foreach (var c in template.Calendars)
    {
        var newId = "cal_" + Guid.NewGuid().ToString("N");
        newCalendarIds[c.Id] = newId;

        isolatedTemplate.Calendars.Add(new SimulatorCalendarModel
        {
            Id = newId,
            Summary = c.Summary,
            BackgroundColor = c.BackgroundColor
        });
    }

    foreach (var e in template.Events)
    {
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = newCalendarIds.TryGetValue(e.CalendarId, out var mappedId) ? mappedId : e.CalendarId,
            Summary = e.Summary,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            IsAllDay = e.IsAllDay
        });
    }

    // Store by userKey so GivenILoginAsTheUser can look up the unique username.
    _scenarioContext[$"UniqueUsername:{userKey}"] = uniqueUsername;
    _scenarioContext["UserTemplate"] = isolatedTemplate;
    await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
}
```

- [ ] **Step 3.2: Add the new activation step after `GivenIHaveAUserLike`**

```csharp
[Given(@"the ""([^""]*)"" calendar is the active calendar")]
public Task GivenTheCalendarIsTheActiveCalendar(string calendarName)
{
    var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
    var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName);

    if (calendar == null)
        throw new Exception(
            $"Calendar '{calendarName}' not found in the user template. " +
            $"Available: {string.Join(", ", isolatedTemplate.Calendars.Select(c => c.Summary))}");

    _scenarioContext["CurrentCalendarId"] = calendar.Id;
    return Task.CompletedTask;
}
```

Add `using System.Linq;` at the top if not already present (needed for `Select`).

- [ ] **Step 3.3: Build to confirm**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Steps/FamilyHQ.E2E.Steps.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

(Note: feature files still reference the old step pattern — they will show as pending/unmatched at runtime until Task 4 and 5 update them. Build still succeeds as Reqnroll validates step bindings at runtime, not compile time.)

- [ ] **Step 3.4: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs
git commit -m "refactor(e2e): split user setup and active calendar selection into separate steps"
```

---

## Task 4: Update `Authentication.feature`

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Features/Authentication.feature`

Three scenarios use `with calendar "Family Events"`. Apply the rules from the spec:

| Scenario | Change |
|----------|--------|
| User can sign in and see their username | Strip `with calendar` only — no unqualified event steps |
| User can sign out and return to sign-in screen | Strip `with calendar` only — no unqualified event steps |
| Calendar is visible when authenticated | Split + add activation (`"Family Events"`) — uses `the user has an all-day event X tomorrow` |

- [ ] **Step 4.1: Apply changes to `Authentication.feature`**

"User can sign in" and "User can sign out" become:
```gherkin
Given I have a user like "TestFamilyMember"
```

"Calendar is visible when authenticated" becomes:
```gherkin
Given I have a user like "TestFamilyMember"
And the "Family Events" calendar is the active calendar
And the user has an all-day event "School Holiday" tomorrow
```

- [ ] **Step 4.2: Build to confirm**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4.3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Features/Authentication.feature
git commit -m "test(e2e): update Authentication.feature to use explicit calendar activation step"
```

---

## Task 5: Update `Dashboard.feature`

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature`

Apply the split pattern. The table below drives every change — check each row off as you go.


| Location | Change |
|----------|--------|
| Background | Split + add activation (`"Family Events"`) |
| View events from multiple calendars | Strip `with calendar` only — no activation |
| View all-day events | Split + add activation (`"Holidays"`) |
| View timed events | Split + add activation (`"Appointments"`) |
| View event details | Split + add activation (`"Family Events"`) |
| Update event after changing its calendar | Strip `with calendar` only |
| Delete event after changing its calendar | Strip `with calendar` only |
| Navigate to next month | Split + add activation (`"Family Events"`) |
| Create event in two calendars appears twice on grid | Strip `with calendar` only |
| Add calendar to existing event via chip | Strip `with calendar` only |
| Remove calendar chip from event | Strip `with calendar` only |
| Last chip is protected | Strip `with calendar` only |
| Delete event removes it from all calendars | Strip `with calendar` only |

**"Split + add activation"** means replace:
```gherkin
Given I have a user like "X" with calendar "Y"
```
with:
```gherkin
Given I have a user like "X"
And the "Y" calendar is the active calendar
```

**"Strip only"** means replace:
```gherkin
Given I have a user like "X" with calendar "Y"
```
with:
```gherkin
Given I have a user like "X"
```

- [ ] **Step 5.1: Apply all changes to `Dashboard.feature` per the table above**

- [ ] **Step 5.2: Build to confirm**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 5.3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Features/Dashboard.feature
git commit -m "test(e2e): update Dashboard.feature to use explicit calendar activation step"
```

---

## Task 6: Update `GoogleCalendarSync.feature` and verify

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature`


All six scenarios use `TestFamilyMember` and all need the activation step — either because they use an unqualified event step (`the user has an all-day event X tomorrow`) or the `When a new event "X" is added to Google Calendar` step (which calls `GetCurrentCalendarId()` internally).

Apply "Split + add activation" to every `Given I have a user like "TestFamilyMember" with calendar "Family Events"` in the file:

```gherkin
Given I have a user like "TestFamilyMember"
And the "Family Events" calendar is the active calendar
```

- [ ] **Step 6.1: Apply the split to all six scenarios in `GoogleCalendarSync.feature`**

- [ ] **Step 6.2: Build to confirm**

```bash
dotnet build tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 6.3: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Features/GoogleCalendarSync.feature
git commit -m "test(e2e): update GoogleCalendarSync.feature to use explicit calendar activation step"
```

- [ ] **Step 6.4: Verify no remaining `with calendar` references in feature files**

```bash
grep -r "with calendar" tests-e2e/FamilyHQ.E2E.Features/
```

Expected: no output (zero matches).

---

## Done

All six tasks complete. The branch is ready for a build pipeline run and E2E test pass before raising a PR to `dev`.
