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
[ Month ] [ Agenda ] [ Day ]
```

### Navigation Header

Identical to the Month view: prev/next month buttons and prev/next year buttons, displaying the current month and year. A **Create** button sits in the header and opens the create modal with today's date pre-filled and no calendar pre-selected.

### Table Structure

| Column | Content |
|--------|---------|
| 1 | Day label: `"ddd dd"` format (e.g., `Mon 01`, `Tue 02`) |
| 2…N+1 | One column per calendar — header shows calendar display name with a small color dot |

- One row per day of the month (28–31 rows)
- **Weekend rows** (Saturday and Sunday) have a subtly different background shade to weekday rows
- **Today's row** is highlighted with a blue tint (matching today-cell treatment in the Month view)
- All days of the month are visible without scrolling. On a 1080×1920 display with ~150px reserved for navigation and tabs, rows are approximately 54–55px tall

---

## Event Cell Display

Each calendar cell for a given day follows these rules:

| Events | Display |
|--------|---------|
| 0 | Empty — tappable to open create modal |
| 1–3 | One plain-text line per event |
| 4+ | First 3 events + `"+N more"` button |

### Event Line Format

- **Timed events:** `"HH:mm {title}"` in 24hr format (e.g., `14:30 Standup`)
- **All-day events:** `"{title}"` only — no time prefix
- **Text colour:** Black
- **Overflow:** Truncated with ellipsis if text exceeds cell width

### Sort Order

All-day events first, then timed events sorted by start time ascending — consistent with the Month view.

### "+N More" Button

Tapping `"+N more"` calls `SwitchToView(DashboardView.Day, date)`, navigating to the Day view for that specific date. Same behaviour as the Month view overflow indicator.

---

## Interaction Model

| Interaction | Behaviour |
|-------------|-----------|
| Tap an event line | Opens edit modal pre-populated with event data |
| Tap an empty calendar cell | Opens create modal with date pre-filled and that calendar pre-selected |
| Tap top-level Create button | Opens create modal with today's date pre-filled, no calendar pre-selected |
| Tap `"+N more"` | Navigates to Day view for that date |
| Tap a tab | Switches view via `SwitchToView()`; current month context preserved |
| Tap prev/next month or year | Updates `CurrentMonth`, calls `LoadMonthDataAsync()`, re-renders table |

No new modals are introduced. All create/edit modal machinery in `Index.razor` is reused as-is.

---

## Data & State

No new API calls or state fields are required.

| Existing state | Usage in Agenda view |
|----------------|---------------------|
| `_monthView` | Per-day event dictionary, populated by `LoadMonthDataAsync()` |
| `_calendars` | Ordered list of calendar columns |
| `CurrentMonth` | Drives which month is displayed |
| `_selectedDate` | Preserved when switching between views |

### Enum Change

```csharp
private enum DashboardView { Month, MonthAgenda, Day }
```

`SwitchToView()` requires no changes — it handles date and month-boundary logic generically.

---

## Testing

All tests are E2E acceptance tests using the existing Reqnroll BDD framework, added as a new feature file: `MonthAgendaView.feature`.

### Navigation Scenarios

- Agenda tab is visible and switches to the agenda view
- Navigating to the previous month updates the displayed days
- Navigating to the next month updates the displayed days
- Navigating to the previous year updates the displayed days
- Navigating to the next year updates the displayed days
- Switching from Agenda to Day view preserves the current month context
- Tapping `"+N more"` navigates to the Day view for the correct date

### Display Scenarios

- All days of the current month are visible
- Weekend rows have a different background shade to weekday rows
- Today's row is highlighted
- Calendar column headers are displayed with correct names
- Timed events display in `"HH:mm {title}"` 24hr format
- All-day events display title only with no time prefix
- Events are sorted: all-day first, then by start time ascending
- A day with more than 3 events shows exactly 3 lines and a `"+N more"` indicator
- An event belonging to two calendars appears in both calendar columns

### Interaction Scenarios

- Tapping an event opens the edit modal
- Tapping an empty cell opens the create modal with the correct date and calendar pre-selected
- Tapping the top-level create button opens the create modal with today's date
- Creating an event via the modal refreshes the agenda view

### Sync Scenarios

- When a Google Calendar sync occurs while the Agenda view is open, the view refreshes automatically via SignalR
- A newly synced event appears in the correct calendar column on the correct day
- A synced event deletion removes the event from the view without a manual refresh
- A synced event update (title or time change) is reflected in the view
