# Design: Index.razor Component Decomposition + Pixel Orbiting

**Date:** 2026-03-28
**Status:** Approved

## Overview

Two related goals:

1. Decompose the monolithic `Index.razor` (~1000 lines) into focused Razor components with clear responsibilities.
2. Implement pixel orbiting — a screen burn-in prevention technique that slowly shifts the entire display by a few pixels every ~120 seconds, suitable for a 27" 1080p portrait touchscreen running continuously.

---

## 1. Component Decomposition

### Motivation

`Index.razor` currently contains three view templates (Month, Agenda, Day), three modals, per-view nav bars, and ~500 lines of C# logic. Decomposing it improves readability, isolates concerns, and makes each piece independently understandable.

### New File Locations

All new components live under `src/FamilyHQ.WebUi/Components/Dashboard/`:

```
Components/
  Dashboard/
    DashboardHeader.razor
    DashboardTabs.razor
    MonthView.razor
    AgendaView.razor
    DayView.razor
    QuickJumpModal.razor
    DayPickerModal.razor
    EventModal.razor
```

`Pages/Index.razor` becomes the **orchestrator only** — it holds all shared state and wires components together. It contains no view markup beyond the top-level layout shell.

### State Ownership

`Index.razor` owns all shared state:

- `_currentView` (DashboardView enum)
- `_isAuthenticated`, `_username`, `_userId`
- `CurrentMonth`
- `IsLoading`
- `_monthView`
- `_weeks`
- `_calendars`
- `_selectedDate`
- Modal visibility flags: `_showQuickJumpModal`, `_jumpYear`, `_showDayPickerModal`, `_pickedDate`

`EventModal` is the exception — it owns its own internal form state (`_eventModel`, `_selectedCalendarIds`, `_modalError`, `_isSaving`) and its own API calls (save/delete), since those concerns are inseparable from the form itself.

### Component Interfaces

#### DashboardHeader
**Parameters:** `string? Username`, `string? UserId`
**Callbacks:** `EventCallback OnSignOut`

#### DashboardTabs
**Parameters:** `DashboardView CurrentView`
**Callbacks:** `EventCallback<DashboardView> OnViewChanged`

#### MonthView
**Parameters:** `DateTime CurrentMonth`, `List<List<DayItem>> Weeks`, `bool IsLoading`, `MonthViewModel? MonthView`
**Callbacks:**
- `EventCallback OnPreviousMonth`
- `EventCallback OnNextMonth`
- `EventCallback OnOpenQuickJump`
- `EventCallback<DateTime> OnAddEvent`
- `EventCallback<CalendarEventViewModel> OnEditEvent`
- `EventCallback<DateTime> OnDayDrillDown`

#### AgendaView
**Parameters:** `DateTime CurrentMonth`, `MonthViewModel? MonthView`, `IReadOnlyList<CalendarSummaryViewModel> Calendars`
**Callbacks:**
- `EventCallback OnPreviousMonth`
- `EventCallback OnNextMonth`
- `EventCallback OnOpenQuickJump`
- `EventCallback<DateTime> OnAddEvent`
- `EventCallback<(DateTime Date, Guid CalendarId)> OnAddEventForCalendar`
- `EventCallback<DateTime> OnDayDrillDown`

#### DayView
**Parameters:** `DateTime SelectedDate`, `MonthViewModel? MonthView`, `IReadOnlyList<CalendarSummaryViewModel> Calendars`
**Callbacks:**
- `EventCallback<DateTime> OnNavigate`
- `EventCallback OnOpenDayPicker`
- `EventCallback<CalendarEventViewModel> OnEditEvent`
- `EventCallback<(DateTime Date, Guid CalendarId)> OnAddEvent`

#### QuickJumpModal
**Parameters:** `bool IsVisible`, `DateTime CurrentMonth`
**Callbacks:**
- `EventCallback<(int Year, int Month)> OnJump`
- `EventCallback OnJumpToday`
- `EventCallback OnClose`

#### DayPickerModal
**Parameters:** `bool IsVisible`, `DateTime InitialDate`
**Callbacks:**
- `EventCallback<DateTime> OnDatePicked`
- `EventCallback OnJumpToday`
- `EventCallback OnClose`

#### EventModal
**Parameters:** `IReadOnlyList<CalendarSummaryViewModel> Calendars`, `ICalendarApiService CalendarApi`
**Public API:** Opened via `@ref` by calling `OpenForCreate(DateTime date, Guid? calendarId)` or `OpenForEdit(CalendarEventViewModel evt)`
**Callbacks:**
- `EventCallback OnSaved`
- `EventCallback OnDeleted`

### E2E Test Compatibility

All existing `data-testid` attributes must be preserved exactly in the rendered HTML output. Component extraction must not alter any attribute values or DOM structure visible to the test suite. This is a hard requirement.

---

## 2. Pixel Orbiting

### Motivation

The display runs continuously on a portrait 27" 1080p touchscreen. Static pixels at fixed positions will burn into the panel over time. Slowly shifting the entire rendered output by a few pixels prevents any single screen pixel from sustaining a persistent image element.

### Approach

A standalone JS module (`pixel-orbit.js`) drives the orbit independently of Blazor. It uses `setInterval` to advance through a fixed sequence of small offsets applied as a CSS `transform: translate(Xpx, Ypx)` on the outermost dashboard container. This produces zero Blazor re-renders.

### Orbit Path

8 positions on a small ellipse (±3px X, ±2px Y), stepped sequentially:

| Step | X  | Y  |
|------|----|----|
| 0    |  0 |  0 |
| 1    |  2 |  1 |
| 2    |  3 |  0 |
| 3    |  2 | -1 |
| 4    |  0 | -2 |
| 5    | -2 | -1 |
| 6    | -3 |  0 |
| 7    | -2 |  1 |

A full cycle completes every ~16 minutes (8 steps × 120s). The display never drifts more than 3px from its natural position.

### Transition

A CSS `transition: transform 2s ease-in-out` on the dashboard container element makes each step a slow, imperceptible glide rather than a sudden jump.

### Interval Jitter

Each step uses 120 seconds ± 15 seconds random jitter, so the shift cadence is not predictable.

### Scope

The orbit transform is applied to `<div class="dashboard-container">` — the outermost element that wraps all views and all modals. Everything on screen shifts together.

### New Files

- `src/FamilyHQ.WebUi/wwwroot/js/pixel-orbit.js` — ES module exporting `init(elementId)`
- A `<script type="module">` reference added to `src/FamilyHQ.WebUi/wwwroot/index.html`

### Blazor Integration

`Index.razor` calls `JS.InvokeVoidAsync("pixelOrbit.init", "dashboard-container")` in `OnAfterRenderAsync` on first render only (`if (firstRender)`). No C# timers, no `StateHasChanged`, no re-render cost.

The `dashboard-container` div receives an `id="dashboard-container"` attribute (it currently has only a class) so the JS module can target it reliably.

---

## Verification

- All existing E2E tests pass without modification.
- The dashboard renders identically to the pre-refactor state.
- The pixel orbit is visually imperceptible during normal use but measurably shifts the transform every ~120 seconds.
