# Month Agenda View — Design Spec

**Date:** 2026-03-27
**Status:** Approved

---

## Overview

Add a third calendar view — **Month Agenda** — to the FamilyHQ dashboard. The view is optimised for a 27" 1080×1920 portrait touchscreen (Raspberry Pi kiosk). It presents all days of the current month as rows in a table, with one column per calendar, allowing the user to see the entire month at a glance without scrolling.

---

## Implementation Approach

**Option A — Inline in Index.razor.** Add the view as a third `@if` block in the existing `Index.razor`, consistent with how the Month and Day views are structured today. No architectural changes. A future refactor (Option C) will extract all three views into separate components.

---

## Layout & Navigation

### Tab Bar

The existing tab bar gains a third tab: **"Agenda"**, positioned between Month and Day:

```
[ Month View ] [ Agenda ] [ Day View ]
```

The existing tab labels **"Month View"** and **"Day View"** are unchanged. Only the new Agenda tab is labelled **"Agenda"**.

As part of this work, all three tab buttons receive `data-testid` attributes. The existing `DashboardPage` locators for Month and Day tabs (currently located by role+name) must be updated to use these testids:

| Tab label | data-testid | Updated `DashboardPage` locator |
|-----------|-------------|----------------------------------|
| Month View | `month-tab` | `Page.GetByTestId("month-tab")` |
| Agenda | `agenda-tab` | `Page.GetByTestId("agenda-tab")` |
| Day View | `day-tab` | `Page.GetByTestId("day-tab")` |

Updating `DashboardPage.MonthTab`, `DashboardPage.DayTab` to use testid locators is in scope for this feature.

### Navigation Header

Identical to the Month view: prev/next month buttons and a centre month/year label that opens the existing `QuickJumpModal` (month and year picker). There are no separate prev/next year buttons — year navigation is handled exclusively via the QuickJump modal, consistent with the Month view. A **Create** button also sits in the header.

| Element | data-testid |
|---------|-------------|
| Prev month button | `agenda-prev-month` |
| Next month button | `agenda-next-month` |
| Month/year label (opens QuickJump modal) | `agenda-month-year-label` |
| Create button | `agenda-create-button` |

**Create button behaviour:** Calls `OpenAddEventModal(DateTime.Today)` — consistent with the existing Month/Day Create button behaviour. The first calendar in `_calendars` is pre-selected by default (the modal's existing fallback).

### Table Structure

The table is wrapped in a `<div class="agenda-view-container">` which is used by `WaitForCalendarVisibleAsync` in `DashboardPage`. That method's locator must be updated to include `.agenda-view-container` alongside `.month-table` and `.day-view-container`:

```csharp
// DashboardPage.WaitForCalendarVisibleAsync — updated locator:
await Page.Locator(".month-table, .day-view-container, .agenda-view-container")
          .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
```

This update ensures that any E2E step that calls `WaitForCalendarVisibleAsync` (including all create/edit/delete flows) works correctly when the Agenda view is active.

| Column | Content | data-testid |
|--------|---------|-------------|
| 1 | Day label: `"ddd dd"` format (e.g., `Mon 01`, `Tue 02`) | `agenda-day-label-{yyyy-MM-dd}` |
| 2…N+1 | One column per calendar | `agenda-calendar-header-{calendarId}` |

Each calendar cell: `data-testid="agenda-cell-{yyyy-MM-dd}-{calendarId}"`

- One row per day of the month (28–31 rows). No padding rows for days outside the month (unlike the Month view which pads to 6 full weeks).
- **Weekend rows** (Saturday and Sunday) carry the CSS class `agenda-weekend-row` and have a subtly different background shade to weekday rows.
- **Today's row** carries the CSS class `agenda-today-row` and is highlighted with a blue tint. When the user navigates to a month that does not contain today, no row is highlighted — this is handled gracefully with no error.
- All days of the month are visible without scrolling. Row height is set via CSS: `height: calc((100vh - 150px) / 31)` ensuring that even a 31-day month fits within the viewport. The 150px constant accounts for the tab bar, navigation header, and table header row. The exact value is verified on the target device during implementation and adjusted if needed.

### Nesting in Index.razor

The Agenda `@if` block is placed inside the same outer null-check guard that wraps the Month and Day views (the `_monthView != null` check at lines 87–101). This ensures `_monthView` is never accessed while null and the `IsLoading` spinner is shown while data is loading.

---

## Event Cell Display

Each calendar cell for a given day displays events **filtered to that specific calendar** — the data source (`_monthView.Days[dateKey]`) contains events from all calendars mixed together. The rendering logic filters by `CalendarInfoId == calendar.Id` before applying the display rules below.

| Events in this calendar cell | Display |
|------------------------------|---------|
| 0 | Empty — cell is tappable |
| 1–3 | One plain-text line per event |
| 4+ | First 3 event lines + `"+N more"` button |

The threshold is fixed at 3 regardless of rendered row height. Three text lines at the target font size (0.75rem) fit within a 54px row.

### Event Line Format

- **Timed events:** `"HH:mm {title}"` in 24hr format (e.g., `14:30 Standup`)
- **All-day events:** `"{title}"` only — no time prefix
- **Text colour:** Black
- **Background:** None — plain text, no capsule or colour indicator on the event line itself. The calendar column header (with its colour dot) serves as the visual calendar indicator.
- **Overflow:** Truncated with ellipsis if text exceeds cell width

**Note on time format:** The Month and Day views use 12-hour format (`h:mm tt`, e.g., "2:30 PM"). The Agenda view **intentionally** uses 24-hour format (`HH:mm`, e.g., "14:30") to reduce character width in the denser grid layout. This divergence is deliberate and should not be treated as a bug.

Each event line has `data-testid="agenda-event-{eventId}-{calendarId}"`.
The `"+N more"` button has `data-testid="agenda-overflow-{yyyy-MM-dd}-{calendarId}"`.

### Multi-Day Events

A multi-day event (e.g., a 3-day holiday) appears on **every day it spans within the current month**. A continuation day is detected by comparing `evt.Start.Date < currentRowDate` — no new helper is needed.

- **First day** (`evt.Start.Date == currentRowDate`): A timed multi-day event shows `"HH:mm {title}"` — the standard timed event rule applies.
- **Continuation days** (`evt.Start.Date < currentRowDate`): Title only, no time prefix — treated as all-day for display purposes.

**Cross-month spanning events:** The Agenda view renders only events present in `_monthView.Days` as returned by the existing API. Whether events that started in a prior month and end in the current month appear in `_monthView.Days` is governed by the existing API query logic — the Agenda view does not change this. Cross-month continuation rendering is out of scope; the view's multi-day behaviour is limited to events whose `Start.Date` falls within the displayed month. This matches the existing Month view's behaviour.

### Sort Order

Within a calendar cell: all-day events first (including multi-day continuation entries), then timed events sorted by `evt.Start` ascending — consistent with the Month view.

### "+N More" Button

Tapping `"+N more"` calls `SwitchToView(DashboardView.Day, date)` where `date` is a `DateTime` derived from the current row's date key string using culture-invariant parsing:

```csharp
DateTime.ParseExact(dateKey, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
```

`DateTime.Parse(dateKey)` must not be used — it is locale-sensitive and will fail in some regional settings. The existing `SwitchToView(DashboardView view, DateTime? date = null)` signature accepts this without changes.

---

## Interaction Model

| Interaction | Behaviour |
|-------------|-----------|
| Tap an event line | Opens edit modal pre-populated with event data |
| Tap cell whitespace (empty or non-empty) | Opens create modal with date pre-filled and that calendar pre-selected |
| Tap top-level Create button | Opens create modal with today's date; first calendar pre-selected |
| Tap `"+N more"` | Calls `SwitchToView(DashboardView.Day, date)` |
| Tap a tab | Calls `SwitchToView()`; current month context preserved |
| Tap prev/next month | Updates `CurrentMonth`, calls `LoadMonthDataAsync()` |
| Tap month/year label | Opens existing `QuickJumpModal` |

No new modals are introduced. All create/edit/quickjump modal machinery in `Index.razor` is reused as-is.

**Cell tap implementation:** The calendar cell `<td>` carries the `@onclick` handler: `@onclick="() => OpenAddEventModal(date, calendar.Id)"`. Individual event line `<div>` elements carry their own `@onclick` handlers for the edit modal and call `@onclick:stopPropagation="true"` to prevent the cell's create handler from firing. This ensures tapping whitespace in any cell (even non-empty ones) opens the create modal, while tapping an event line opens the edit modal.

**Delta from Month view:** The Month view calls `OpenAddEventModal(day.Date)` with no `calendarId` argument (modal defaults to first calendar). The Agenda view cell tap calls `OpenAddEventModal(date, calendar.Id)` — passing the tapped column's calendar ID explicitly. This is intentional: the column context makes the pre-selection meaningful.

### Loading State

The Agenda view inherits the existing shared `IsLoading` spinner. The Agenda `@if` block is nested inside the same null-check guard as the Month and Day views, so it is never rendered while `_monthView` is null.

---

## Data & State

No new API calls or state fields are required.

| Existing state | Usage in Agenda view |
|----------------|---------------------|
| `_monthView` | Per-day event dictionary (`Dictionary<string, List<CalendarEventViewModel>>`), populated by `LoadMonthDataAsync()` |
| `_calendars` | Ordered list of calendars — defines column order |
| `CurrentMonth` | Drives which month is displayed |
| `_selectedDate` | Preserved when switching between views |

### Per-Cell Data Grouping

To render a cell for date `d` (string key `"yyyy-MM-dd"`) and calendar `c`:

1. Lookup `_monthView.Days[d]` to get all events for that day across all calendars
2. Filter by `evt.CalendarInfoId == c.Id`
3. Sort: all-day / continuation-day entries first, then by `evt.Start` ascending
4. Take first 3 for display; compute overflow count for `"+N more"`

### Enum Change

```csharp
private enum DashboardView { Month, MonthAgenda, Day }
```

`MonthAgenda` is inserted at position 1 (value = 1). `Month` remains 0. `Day` becomes 2. All comparisons in the codebase use named equality — no numeric checks or range guards exist. This is safe. `SwitchToView()` requires no changes.

The existing `OnAfterRenderAsync` scroll logic (`if (_currentView == DashboardView.Day)`) is unaffected — `MonthAgenda` correctly falls into the `else` (non-Day) branch, which resets scroll tracking. No changes are needed to that method.

---

## Files Changed

| File | Change |
|------|--------|
| `Index.razor` | **(1)** Add `MonthAgenda` to the `private enum DashboardView` declaration in the `@code` block: `private enum DashboardView { Month, MonthAgenda, Day }`. **(2)** Add third `@if` block for the agenda view inside the existing null-check guard. |
| `app.css` | Add `.agenda-view-container`, `.agenda-weekend-row`, `.agenda-today-row` styles |
| `DashboardPage.cs` | **(1)** Update `WaitForCalendarVisibleAsync` locator to `Page.Locator(".month-table, .day-view-container, .agenda-view-container").First`. **(2)** Update `MonthTab` property to `Page.GetByTestId("month-tab")`. **(3)** Update `DayTab` property to `Page.GetByTestId("day-tab")`. |
| `MonthAgendaView.feature` | New feature file with 18 scenarios |
| New step definitions | New step signatures listed below |

---

## Testing

All tests are E2E acceptance tests using the existing Reqnroll BDD framework, added as a new feature file: `MonthAgendaView.feature`.

### New Step Definitions Required

The existing step patterns use relative dates ("tomorrow"). Agenda view scenarios use absolute dates (`"yyyy-MM-dd"`) for precision. The following new step signatures must be authored:

| Step pattern | Notes |
|---|---|
| `the user has a timed event "{title}" at "{HH:mm}" on "{yyyy-MM-dd}" in "{calendar}"` | Absolute-date variant of existing timed event step |
| `the user has an all-day event "{title}" on "{yyyy-MM-dd}" in "{calendar}"` | Absolute-date variant of existing all-day step |
| `the user has {n} events on "{yyyy-MM-dd}" in "{calendar}"` | Creates N auto-named events on a specific date |
| `the user has an event "{title}" in calendars "{cal1}" and "{cal2}" on "{yyyy-MM-dd}"` | Absolute-date multi-calendar step |
| `the user has no events on "{yyyy-MM-dd}"` | Setup guard — ensures no events exist for that date |
| `a sync adds the event "{title}" to "{calendar}" on "{yyyy-MM-dd}"` | Triggers simulator webhook for a new event |
| `a sync deletes the event "{title}" from "{calendar}"` | Triggers simulator webhook for event deletion |
| `a sync updates the event "{title}" to have the title "{newTitle}"` | Triggers simulator webhook for event update |

### Navigation Scenarios

```gherkin
Scenario: Agenda tab is visible and navigates to the agenda view
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see the month agenda view

Scenario: Navigating to the previous month updates the displayed days
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I navigate to the previous month on the agenda view
  Then I see the days of the previous month on the agenda view

Scenario: Navigating to the next month updates the displayed days
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I navigate to the next month on the agenda view
  Then I see the days of the next month on the agenda view

Scenario: Switching from Agenda view to Day view preserves the month context
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I click the "Day View" tab
  Then I see the day view for the current month

Scenario: Tapping "+N more" navigates to the day view for the correct date
  Given I have a user like "OverflowUser"
  And the user has 4 events on "2026-03-15" in "Work Calendar"
  And I login as the user "OverflowUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I tap the overflow indicator for "2026-03-15" in "Work Calendar"
  Then I see the day view for "2026-03-15"
```

### Display Scenarios

```gherkin
Scenario: All days of the current month are visible without scrolling
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then all days of the current month are visible on the agenda view

Scenario: Weekend rows have a different background shade to weekday rows
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then weekend rows on the agenda view have the CSS class "agenda-weekend-row"
  And weekday rows on the agenda view do not have the CSS class "agenda-weekend-row"

Scenario: Today's row is highlighted
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then today's row on the agenda view has the CSS class "agenda-today-row"

Scenario: Calendar column headers display the correct calendar names
  Given I have a user like "MultiCalUser"
  And I login as the user "MultiCalUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see a column header for "Work Calendar"
  And I see a column header for "Personal Calendar"

Scenario: Timed events display in 24hr HH:mm format
  Given I have a user like "StandardUser"
  And the user has a timed event "Standup" at "14:30" on "2026-03-15" in "Work Calendar"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see the event "14:30 Standup" in the "Work Calendar" column for "2026-03-15"

Scenario: All-day events display title only with no time prefix
  Given I have a user like "StandardUser"
  And the user has an all-day event "Bank Holiday" on "2026-03-17" in "Work Calendar"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see the event "Bank Holiday" in the "Work Calendar" column for "2026-03-17"
  And I do not see a time prefix for the event "Bank Holiday"

Scenario: A day with more than 3 events shows 3 lines and a "+N more" indicator
  Given I have a user like "OverflowUser"
  And the user has 4 events on "2026-03-15" in "Work Calendar"
  And I login as the user "OverflowUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see 3 event lines in the "Work Calendar" column for "2026-03-15"
  And I see a "+1 more" indicator in the "Work Calendar" column for "2026-03-15"

Scenario: An event in two calendars appears in both calendar columns
  Given I have a user like "MultiCalUser"
  And the user has an event "Team Meeting" in calendars "Work Calendar" and "Personal Calendar" on "2026-03-20"
  And I login as the user "MultiCalUser"
  And I view the dashboard
  When I click the "Agenda" tab
  Then I see the event "Team Meeting" in the "Work Calendar" column for "2026-03-20"
  And I see the event "Team Meeting" in the "Personal Calendar" column for "2026-03-20"
```

### Interaction Scenarios

```gherkin
Scenario: Tapping an event opens the edit modal
  Given I have a user like "StandardUser"
  And the user has a timed event "Standup" at "09:00" on "2026-03-15" in "Work Calendar"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I tap the event "Standup" in the "Work Calendar" column for "2026-03-15"
  Then I see the edit event modal for "Standup"

Scenario: Tapping an empty cell opens the create modal with the correct date and calendar pre-selected
  Given I have a user like "StandardUser"
  And the user has no events on "2026-03-25"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I tap the empty cell in the "Work Calendar" column for "2026-03-25"
  Then I see the create event modal
  And the date is pre-filled with "2026-03-25"
  And "Work Calendar" is pre-selected

Scenario: Tapping the top-level create button opens the create modal with today's date
  Given I have a user like "StandardUser"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I tap the agenda create button
  Then I see the create event modal
  And the date is pre-filled with today's date

Scenario: Creating an event via the modal refreshes the agenda view
  Given I have a user like "StandardUser"
  And the user has no events on "2026-03-20"
  And I login as the user "StandardUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And I tap the empty cell in the "Work Calendar" column for "2026-03-20"
  And I create the event "New Meeting" via the modal
  Then I see the event "New Meeting" in the "Work Calendar" column for "2026-03-20"
```

### Sync Scenarios

```gherkin
Scenario: A newly synced event appears in the correct calendar column
  Given I have a user like "SyncUser"
  And I login as the user "SyncUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And a sync adds the event "Synced Meeting" to "Work Calendar" on "2026-03-18"
  Then I see the event "Synced Meeting" in the "Work Calendar" column for "2026-03-18"

Scenario: A synced event deletion removes the event from the agenda view
  Given I have a user like "SyncUser"
  And the user has a timed event "Standup" at "09:00" on "2026-03-18" in "Work Calendar"
  And I login as the user "SyncUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And a sync deletes the event "Standup" from "Work Calendar"
  Then I do not see the event "Standup" in the "Work Calendar" column for "2026-03-18"

Scenario: A synced event update is reflected in the agenda view
  Given I have a user like "SyncUser"
  And the user has a timed event "Old Title" at "09:00" on "2026-03-18" in "Work Calendar"
  And I login as the user "SyncUser"
  And I view the dashboard
  When I click the "Agenda" tab
  And a sync updates the event "Old Title" to have the title "New Title"
  Then I see the event "New Title" in the "Work Calendar" column for "2026-03-18"
  And I do not see the event "Old Title" in the "Work Calendar" column for "2026-03-18"
```
