# UI Fixes & Settings E2E Tests — Design

**Date:** 2026-03-30

## Overview

Four targeted fixes on the `feature/ui-redesign-2` branch:

1. **Settings back button** — `DashboardHeader` gains a `BackUrl` parameter; Settings page renders it with a `← Back` link and an auth guard.
2. **Month view overflow** — CSS-only fix: `table-layout: fixed` + event capsule truncation + container overflow guard.
3. **Button theming** — CSS overrides in `app.css` replace Bootstrap blue with the ambient theme colour palette on all non-danger buttons.
4. **Settings E2E tests** — Full BDD acceptance test suite covering all Settings page behaviour.

---

## 1. Settings Back Button

### Problem
`Settings.razor` is a standalone page with no navigation back to the dashboard. Users must use the browser back button.

Additionally, an unauthenticated user who navigates directly to `/settings` will trigger API calls that return 401 with no graceful redirect.

### Solution

**`DashboardHeader.razor`** gains one optional parameter:

```
[Parameter] string? BackUrl
```

Rendering logic:
- When `BackUrl` is **null** (default): current behaviour — `FamilyHQ` brand on left, gear icon link to `/settings` on right.
- When `BackUrl` is **set**: left side renders a `← Back` link to `BackUrl`; right side renders nothing (gear icon hidden).

The back link uses a new `.dashboard-header__back` CSS class styled identically to `.dashboard-header__settings` (48×48 touch target, rounded, `color: var(--theme-text)`, hover fills `var(--theme-accent)`), but contains a left-arrow SVG instead of the gear.

**`Settings.razor`** changes:
- Adds `<DashboardHeader BackUrl="/" />` as the first element inside the page root.
- Adds an auth guard at the top of `OnInitializedAsync`:
  ```csharp
  if (!await AuthService.IsAuthenticatedAsync())
  {
      Navigation.NavigateTo("/");
      return;
  }
  ```
  This enables the E2E "unauthenticated redirect" scenario and prevents a 401 crash on page load.

### Files changed
- `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor`
- `src/FamilyHQ.WebUi/wwwroot/css/app.css` (`.dashboard-header__back` class)
- `src/FamilyHQ.WebUi/Pages/Settings.razor`

---

## 2. Month View Overflow Fix

### Problem
`.month-table` has no `table-layout: fixed`. Without it, table cells ignore their declared `width: 14.28%` and expand to fit content. Long event titles in a cell push that column wider, causing the table to overflow the viewport at portrait 1080p.

### Solution

Three CSS changes in `app.css`, no component changes:

1. **`table-layout: fixed`** on `.month-table` — enforces column widths strictly.
2. **`overflow: hidden; text-overflow: ellipsis; white-space: nowrap`** on `.event-capsule` — long titles truncate inside the cell rather than expanding it.
3. **`overflow-x: hidden`** on `.dashboard-container` — safety net against any other overflow source.

### Files changed
- `src/FamilyHQ.WebUi/wwwroot/css/app.css`

---

## 3. Button Theming

### Problem
All calendar view buttons (Add Event, Previous Month, Next Month, modal Save/Cancel) use Bootstrap utility classes (`btn-primary`, `btn-outline-primary`, `btn-light`, `btn-secondary`). These render in Bootstrap's default blue, which conflicts with the ambient time-of-day theme palette.

### Solution

CSS overrides appended to `app.css` after the Bootstrap styles. No component files change. All overrides include a `45s ease-in-out` transition on `background-color`, `border-color`, and `color` to participate in the theme crossfade.

| Class | Background | Border | Text |
|---|---|---|---|
| `.btn-primary` | `var(--theme-accent)` | `var(--theme-accent)` | `var(--theme-on-accent)` |
| `.btn-primary:hover` | `var(--theme-accent-hover)` | `var(--theme-accent-hover)` | `var(--theme-on-accent)` |
| `.btn-outline-primary` | transparent | `var(--theme-accent)` | `var(--theme-accent)` |
| `.btn-outline-primary:hover` | `var(--theme-accent)` | `var(--theme-accent)` | `var(--theme-on-accent)` |
| `.btn-light` | `var(--theme-surface)` | `var(--theme-border)` | `var(--theme-text)` |
| `.btn-secondary` | `var(--theme-surface)` | `var(--theme-border)` | `var(--theme-text-muted)` |

`.btn-outline-danger` is **not overridden** — red is intentional for the destructive delete action.

### Files changed
- `src/FamilyHQ.WebUi/wwwroot/css/app.css`

---

## 4. Settings E2E Tests

### New files
- `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature`
- `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs`
- `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`

### `SettingsPage.cs` — Page Object

Locators:
- `BackBtn` — `.dashboard-header__back`
- `LocationHint` — `.settings-hint` containing "No location saved"
- `LocationPill` — `.settings-location-pill`
- `LocationPillBadge` — `.settings-location-pill__badge`
- `PlaceNameInput` — `#place-input`
- `SaveLocationBtn` — `.settings-btn:not(.settings-btn--secondary)` (the primary save button)
- `MorningTile` — `.theme-tile--morning`
- `DaytimeTile` — `.theme-tile--daytime`
- `EveningTile` — `.theme-tile--evening`
- `NightTile` — `.theme-tile--night`
- `AccountName` — `.account-name`
- `SignOutBtn` — button with text "Sign Out" within `.settings-account`

### `Settings.feature` — Scenarios

```gherkin
Feature: Settings Page
  As an authenticated user
  I want to manage my settings
  So that I can configure location, view my theme schedule, and sign out

  Scenario: Unauthenticated user is redirected to login
    Given I am not authenticated
    When I navigate to the settings page
    Then I see the "Login to Google" button

  Scenario: Back button returns user to dashboard
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    When I click the back button
    Then I see the calendar displayed

  Scenario: No saved location hint is shown when no location is set
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    Then I see the no saved location hint

  Scenario: User can save a location
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    When I enter "Edinburgh, Scotland" as the place name
    And I click save location
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Saved" badge on the location pill

  Scenario: Theme schedule tiles are all visible
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    Then I see the Morning theme tile with a time
    And I see the Daytime theme tile with a time
    And I see the Evening theme tile with a time
    And I see the Night theme tile with a time

  Scenario: Account section shows the signed-in username
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    Then I see the username in the account section

  Scenario: User can sign out from the settings page
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page
    When I click the sign out button on the settings page
    Then I see the "Login to Google" button
```

### `SettingsSteps.cs` — Step Bindings

Reuses existing `[Given]` steps:
- `GivenIHaveAUserLike` — from `UserSteps.cs`
- `GivenIAmSignedInAsTheUser` — from `AuthenticationSteps.cs`
- `ThenISeeTheButton("Login to Google")` — from `AuthenticationSteps.cs`
- `ThenISeeTheCalendarDisplayed` — from `AuthenticationSteps.cs`

New `[Given]` step:
- `Given I am on the settings page` — navigates to `/settings`, waits for `.settings-page` to be visible

New `[When]` steps:
- `When I navigate to the settings page` — `page.GotoAsync(config.BaseUrl + "/settings")`
- `When I click the back button` — `settingsPage.BackBtn.ClickAsync()`
- `When I enter "…" as the place name` — fills `PlaceNameInput`
- `When I click save location` — clicks `SaveLocationBtn`, waits for network idle
- `When I click the sign out button on the settings page` — `settingsPage.SignOutBtn.ClickAsync()`, waits for redirect

New `[Then]` steps:
- `Then I see the no saved location hint` — waits for `LocationHint` visible
- `Then I see the location pill displaying "…"` — waits for `LocationPill` containing the text
- `Then I see the "Saved" badge on the location pill` — checks `LocationPillBadge` text equals "Saved"
- `Then I see the Morning/Daytime/Evening/Night theme tile with a time` — waits for tile visible, asserts `.theme-tile__time` is non-empty
- `Then I see the username in the account section` — waits for `AccountName` visible and non-empty

---

## Constraints & Notes

- `.btn-outline-danger` (modal delete button) is intentionally excluded from theming overrides.
- The `BackUrl` parameter on `DashboardHeader` defaults to null; all existing usages on the dashboard page require no change.
- The Settings E2E "save location" test hits the real geocoding API (Nominatim) via the backend — this is intentional and follows the project's no-mock-database rule.
- `SettingsSteps.cs` does not duplicate any existing step text — new step phrases are distinct from those in `AuthenticationSteps.cs` and `UserSteps.cs`.
