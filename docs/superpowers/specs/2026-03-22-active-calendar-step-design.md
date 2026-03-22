# Design: Separate User Setup from Active Calendar Selection

**Date:** 2026-03-22
**Status:** Approved

## Problem

The step `Given I have a user like "X" with calendar "Y"` conflates two concerns:
1. Creating the test user from a template
2. Selecting which calendar is "active" for subsequent unqualified event steps

The `calendarName` parameter was used solely to set `CurrentCalendarId` in the scenario context, which event steps like `the user has an all-day event X tomorrow` (no calendar specified) consume. This coupling is not obvious, and is redundant for single-calendar users where the choice is already determined by the template.

## Goal

Split user setup and active calendar selection into distinct, explicit steps. Fail fast with a clear message if a step requires an active calendar but none has been selected.

## Design

### Step Changes (`UserSteps.cs`)

**Before:**
```gherkin
Given I have a user like "TestFamilyMember" with calendar "Family Events"
```

**After:**
```gherkin
Given I have a user like "TestFamilyMember"
And the "Family Events" calendar is the active calendar
```

#### `GivenIHaveAUserLike(string userKey)`
- Replaces `GivenIHaveAUserLikeWithCalendar(string userKey, string calendarName)`
- Generates unique username, builds isolated template, registers with simulator
- Does **not** set `CurrentCalendarId`

#### `GivenTheCalendarIsTheActiveCalendar(string calendarName)` (new step)
- Pattern: `[Given(@"the ""([^""]*)"" calendar is the active calendar")]`
- Retrieves the isolated template from `_scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate")`
- Looks up the named calendar by `Summary` in that template's `Calendars` list
- Sets `_scenarioContext["CurrentCalendarId"]` to the matching calendar's ID
- Throws a descriptive exception if the calendar name is not found in the template

### Fail-Fast Helper (`ScenarioContextExtensions.cs`, new file in `FamilyHQ.E2E.Steps`)

```csharp
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

All `_scenarioContext.Get<string>("CurrentCalendarId")` calls in `EventSteps.cs` and `WebhookDataSteps.cs` are replaced with `_scenarioContext.GetCurrentCalendarId()`.

### Feature File Changes

#### Rule: when to add the activation step

Add `And the "X" calendar is the active calendar` after the user setup step when **either**:
- The scenario (or its Background) uses an unqualified event step (`the user has an all-day event X tomorrow`, `the user has a timed event X tomorrow at Y`, `the user has an all-day event X in N days`), **or**
- The scenario uses the `When a new event "X" is added to Google Calendar` step (which also consumes `CurrentCalendarId` via `WebhookDataSteps`)

Omit the activation step when all event setup steps are explicitly named (`in "Calendar Name"`) and no webhook add-event step is used.

#### `Dashboard.feature` — Background

Needs activation step (inheriting `the user has an all-day event "School Holiday" tomorrow` is unqualified):

```gherkin
Background:
  Given I have a user like "TestFamilyMember"
  And the "Family Events" calendar is the active calendar
  And the user has an all-day event "School Holiday" tomorrow
  And I login as the user "TestFamilyMember"
```

#### `Dashboard.feature` — Per-scenario breakdown

| Scenario | Needs activation step? | Reason |
|----------|----------------------|--------|
| View upcoming events on the dashboard month view | No (inherits Background) | Background already activates |
| Create a new event | No (inherits Background) | Background already activates |
| Update an existing event | No (inherits Background) | Background already activates |
| Delete an existing event | No (inherits Background) | Background already activates |
| View events from multiple calendars | No | All event steps are explicitly named |
| View all-day events | Yes | `the user has an all-day event X tomorrow` (unqualified) |
| View timed events | Yes | `the user has a timed event X tomorrow at Y` (unqualified) |
| View event details | Yes | `the user has an all-day event X tomorrow` (unqualified) |
| Update event after changing its calendar | No | All event steps are explicitly named |
| Delete event after changing its calendar | No | All event steps are explicitly named |
| Navigate to next month | Yes | `the user has an all-day event X in N days` (unqualified) |
| Create event in two calendars appears twice on grid | No | No event setup steps; create is via UI |
| Add calendar to existing event via chip | No | All event steps are explicitly named |
| Remove calendar chip from event | No | All event steps are explicitly named |
| Last chip is protected | No | All event steps are explicitly named |
| Delete event removes it from all calendars | No | All event steps are explicitly named |

#### `GoogleCalendarSync.feature`

All six scenarios need the activation step. Two reasons apply across the file:
- Scenarios that use `the user has an all-day event X tomorrow` (unqualified)
- Scenarios that use `When a new event "X" is added to Google Calendar` (which calls `GetCurrentCalendarId()` via `WebhookDataSteps`)

```gherkin
Scenario: New event added in Google Calendar appears on dashboard after sync
  Given I have a user like "TestFamilyMember"
  And the "Family Events" calendar is the active calendar
  And I login as the user "TestFamilyMember"
  ...
```

## Files Changed

| File | Change |
|------|--------|
| `tests-e2e/FamilyHQ.E2E.Steps/UserSteps.cs` | Remove `with calendar` param; add `GivenTheCalendarIsTheActiveCalendar` step |
| `tests-e2e/FamilyHQ.E2E.Steps/ScenarioContextExtensions.cs` | New file — `GetCurrentCalendarId()` extension method |
| `tests-e2e/FamilyHQ.E2E.Steps/EventSteps.cs` | Replace all `Get<string>("CurrentCalendarId")` with `GetCurrentCalendarId()` |
| `tests-e2e/FamilyHQ.E2E.Steps/WebhookDataSteps.cs` | Replace `Get<string>("CurrentCalendarId")` with `GetCurrentCalendarId()` |
| `tests-e2e/FamilyHQ.E2E.Features/WebUi/Dashboard.feature` | Split all `with calendar` steps; add activation steps per table above |
| `tests-e2e/FamilyHQ.E2E.Features/WebUi/GoogleCalendarSync.feature` | Split all `with calendar` steps; add activation step to all scenarios |

## Out of Scope

- Changes to `user_templates.json` — no default flag needed
- Changes to any other step files not listed above
