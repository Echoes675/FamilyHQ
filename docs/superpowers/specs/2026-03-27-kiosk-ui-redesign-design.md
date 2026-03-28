# Kiosk UI Redesign — Design Spec

**Date**: 2026-03-27
**Branch**: feature/kiosk-ui-redesign
**Status**: Approved — open questions resolved 2026-03-27

---

## Core Principle

FamilyHQ is a family kiosk, not a web app. Every design decision must serve a 27" portrait touchscreen mounted on a wall, running on a Raspberry Pi, viewed from across a room. Legibility at distance, touch targets sized for fingers not cursors, and zero-scroll layouts are non-negotiable. The ambient background system makes the display feel alive and contextually aware without burning the screen or taxing the Pi's GPU.

---

## 1. Overview & Goals

### Problem

The current FamilyHQ UI is a standard Blazor WASM web application styled with Bootstrap. It was designed for desktop browser use: small touch targets, scrollable layouts, a traditional nav sidebar, and no ambient awareness. Deployed on a 27" portrait kiosk, it is:

- Unreadable at distance (small font sizes, low contrast against white backgrounds)
- Unusable by touch (Bootstrap buttons are 38px, below the 64px minimum for reliable finger taps)
- Visually inert (static white background, no time or weather awareness)
- Incomplete (no Week View, no RRULE recurrence, no user preferences persistence)
- Fragile for kiosk use (no burn-in protection, no offline resilience)

### Target Hardware / Environment

| Property | Value |
|----------|-------|
| Display | 27" Elo IntelliTouch portrait touchscreen |
| Resolution | 1080 × 1920 px |
| Compute | Raspberry Pi (ARM, limited GPU) |
| OS | Raspberry Pi OS (Chromium kiosk mode) |
| Network | Local LAN, backend on same network |
| Interaction | Finger touch only (no mouse/keyboard in normal use) |

### Success Criteria

1. All three views (Today, Week, Month) render without scrolling on a 1920px-tall viewport.
2. Every interactive element has a minimum 64 × 64 px touch target.
 3. The ambient background transitions correctly through all 4 circadian states and 7 weather overlays.
4. RRULE recurring events are stored, expanded, and displayed correctly.
5. User preferences (column order, colors, density) survive a page reload via LocalStorage.
6. Burn-in protection pixel orbit runs every 120 seconds.
7. The Raspberry Pi sustains 60 fps on the ambient background with no `backdrop-filter: blur` on the main UI layer.

---

## 2. Architecture Changes

### Layer Architecture

The existing clean architecture is preserved. No layer boundary rules change.

```
┌──────────────────────────────────────────────────────────────┐
│  Google Calendar API / Simulator                             │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP JSON
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  GoogleCalendarClient  (FamilyHQ.Services)                   │
│  Maps Google API JSON → domain models                        │
└───────────────────────────┬──────────────────────────────────┘
                            │ CalendarEvent, CalendarInfo
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Service Layer  (FamilyHQ.Services)                          │
│  CalendarEventService + new WeatherService + RruleExpander   │
└───────────────────────────┬──────────────────────────────────┘
                            │ domain models + WeatherState
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  API Controllers  (FamilyHQ.WebApi)                          │
│  New: WeatherHub (SignalR), PreferencesController            │
│  Maps domain → DTOs                                          │
└───────────────────────────┬──────────────────────────────────┘
                            │ HTTP JSON + SignalR
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  CalendarApiService + WeatherApiService  (FamilyHQ.WebUi)    │
│  Maps DTOs → ViewModels                                      │
└───────────────────────────┬──────────────────────────────────┘
                            │ ViewModels
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  Blazor Components  (FamilyHQ.WebUi)                         │
│  DashboardHeader, TodayView, WeekView, CalendarGrid,         │
│  AmbientBackground, EventModal, SettingsPanel                │
└──────────────────────────────────────────────────────────────┘
```

### New Razor Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `DashboardHeader.razor` | `WebUi/Components/` | Year/month nav, view switcher, weather indicator |
| `TodayView.razor` | `WebUi/Components/` | 24-hour time-blocked grid, Outlook-style tiles |
| `WeekView.razor` | `WebUi/Components/` | 7-day hybrid grid (Mon–Sun) |
| `CalendarGrid.razor` | `WebUi/Components/` | Month view: 31-row vertical list, columns = calendars |
| `AmbientBackground.razor` | `WebUi/Components/` | Circadian gradient + weather overlay CSS state machine |
| `EventModal.razor` | `WebUi/Components/` | Create/edit event with RRULE picker |
| `RecurrenceEditPrompt.razor` | `WebUi/Components/` | "This instance vs all" modal for recurring event edits |
| `DeleteConfirmModal.razor` | `WebUi/Components/` | "Are you sure?" deletion confirmation |
| `DayDetailModal.razor` | `WebUi/Components/` | Full event list for a day/person (overflow from +N badge) |
| `MonthQuickJumpModal.razor` | `WebUi/Components/` | 12-month grid for direct month selection |
| `SettingsPanel.razor` | `WebUi/Components/` | Column order, visibility, colors, density, preferences |
| `BurnInOrbit.razor` | `WebUi/Components/` | 1–2px pixel orbit for burn-in protection |

### Changes to Existing Components

| File | Change |
|------|--------|
| `Pages/Index.razor` | Gutted and rebuilt as a thin shell: renders `AmbientBackground`, `DashboardHeader`, and the active view component. All view logic moves to dedicated components. |
| `Layout/MainLayout.razor` | Remove Bootstrap `page`/`content` wrapper. Full-bleed `position: fixed; inset: 0` layout for kiosk. Hosts `BurnInOrbit`. |
| `Layout/NavMenu.razor` | **Deleted.** Navigation moves into `DashboardHeader`. |
| `wwwroot/index.html` | Remove Bootstrap CSS link. Add Tailwind CSS output link. |
| `wwwroot/css/app.css` | Retained for Blazor error UI and loading spinner only. All component styles migrate to Tailwind utility classes or scoped `.razor.css` files. |

### New Backend Services / Interfaces

| Item | Project | Purpose |
|------|---------|---------|
| `IWeatherProvider` | `FamilyHQ.Core/Interfaces/` | Provider-agnostic weather polling contract |
| `WeatherCondition` enum | `FamilyHQ.Core/Models/` | Clear, Cloudy, LightRain, HeavyRain, Thunder, Snow, WindMist |
| `WeatherState` record | `FamilyHQ.Core/Models/` | Current condition + temperature + last-updated |
| `WeatherBackgroundService` | `FamilyHQ.Services/Weather/` | Polls `IWeatherProvider`, caches state, pushes via SignalR |
| `SimulatorWeatherProvider` | `FamilyHQ.Services/Weather/` | Dev/test `IWeatherProvider` that calls simulator mock endpoint |
| `WeatherHub` | `FamilyHQ.WebApi/Hubs/` | SignalR hub; broadcasts `WeatherState` to all connected clients |
| `IRruleExpander` | `FamilyHQ.Core/Interfaces/` | Expands RRULE recurring events into concrete instances for a date range |
| `RruleExpander` | `FamilyHQ.Services/Calendar/` | Implements `IRruleExpander` using iCal.Net or equivalent |
| `IUserPreferencesService` | `FamilyHQ.Core/Interfaces/` | Load/save `UserPreferences` |
| `UserPreferencesService` | `FamilyHQ.Services/` | Persists preferences to DB; used by `PreferencesController` |
| `PreferencesController` | `FamilyHQ.WebApi/Controllers/` | `GET /api/preferences`, `PUT /api/preferences` |
| `ISolarCalculator` | `FamilyHQ.Core/Interfaces/` | Computes sunrise/sunset for a given date and location (NOAA algorithm) |
| `SolarCalculator` | `FamilyHQ.Services/Circadian/` | NOAA solar algorithm implementation (pure math, no external API) |
| `CircadianStateService` | `FamilyHQ.Services/Circadian/` | Background service; runs daily at midnight to compute and store circadian boundaries |
| `CircadianBoundaries` model | `FamilyHQ.Core/Models/` | date, sunrise, sunset, dawnStart, duskEnd — stored in DB |
| `CircadianController` | `FamilyHQ.WebApi/Controllers/` | `GET /api/circadian/current` |
| `KioskOptions` | `FamilyHQ.Services/Options/` | Config class with `Latitude` and `Longitude` properties |

### New API Endpoints

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/api/preferences` | Load user preferences |
| `PUT` | `/api/preferences` | Save user preferences |
| `GET` | `/api/events/range?start=&end=` | Fetch events for a date range (Today/Week views need this; month endpoint already exists) |
| `GET` | `/api/circadian/current` | Returns current circadian state (Day/Dawn/Dusk/Night) — polled by frontend every 5 min |
| SignalR | `/hubs/weather` | Push `WeatherState` updates to frontend |
| `POST` | `/api/simulator/weather` | Simulator only — set mock weather state for E2E testing |
| `GET` | `/api/simulator/weather` | Simulator only — get current mock weather state |

### Database Schema Changes

#### New columns on `CalendarEvent`

| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| `RecurrenceRule` | `text` | Yes | RRULE string (e.g. `RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR`) |
| `RecurrenceId` | `text` | Yes | Google's `recurringEventId` — links an exception instance back to its series master |
| `IsRecurrenceException` | `bool` | No, default `false` | `true` when this row is a "this instance only" edit of a recurring series |

#### New table: `UserPreferences`

| Column | Type | Nullable | Purpose |
|--------|------|----------|---------|
| `Id` | `uuid` | No | PK |
| `UserId` | `text` | No | FK to user identity |
| `ColumnOrder` | `text` | Yes | JSON array of `CalendarInfoId` GUIDs in display order |
| `HiddenCalendarIds` | `text` | Yes | JSON array of hidden `CalendarInfoId` GUIDs |
| `CalendarColors` | `text` | Yes | JSON object: `{ "calendarInfoId": "#hexcolor" }` |
| `EventDensity` | `int` | No, default `2` | Max event rows per cell (1–3) |
| `DefaultView` | `text` | No, default `"Today"` | `"Today"`, `"Week"`, or `"Month"` |
| `AnimatedBackground` | `bool` | No, default `true` | Whether weather CSS animations are enabled |

---

## 3. Visual Design System

### Kiosk Layout (1080 × 1920 px, Portrait)

```
┌─────────────────────────────────────────┐  ← 1080px wide
│  DashboardHeader                        │  ← 120px tall
│  [Year ◄ ►] [Month ◄ ►] [Today|Week|Mo] │
├─────────────────────────────────────────┤
│                                         │
│  Active View                            │  ← 1800px tall (1920 - 120)
│  (TodayView / WeekView / CalendarGrid)  │
│                                         │
└─────────────────────────────────────────┘
│  Auth strip (fixed bottom-right)        │  ← overlaid, not in flow
└─────────────────────────────────────────┘
```

The `AmbientBackground` is `position: fixed; inset: 0; z-index: 0`. The main UI sits at `z-index: 10` inside a `bg-slate-950/85` legibility layer. No element in the main UI uses `backdrop-filter: blur` (Pi GPU constraint).

### Tailwind CSS Integration

**Approach**: Tailwind Play CDN — add a single `<script src="https://cdn.tailwindcss.com"></script>` tag to `wwwroot/index.html`. No NuGet package, no Node.js build step, no MSBuild target, no `tailwind.config.js` file required.

**Decision rationale**: Play CDN is sufficient for kiosk use (single device, LAN-connected, no production CDN latency concerns). Eliminates all build tooling complexity.

Configuration (inline in `index.html` via `tailwind.config` script block):
```html
<script>
  tailwind.config = {
    theme: {
      extend: {
        fontFamily: {
          sans: ['ui-sans-serif', 'system-ui', '-apple-system', 'BlinkMacSystemFont',
                 '"Segoe UI"', 'Roboto', '"Helvetica Neue"', 'Arial', 'sans-serif'],
        },
        colors: {
          kiosk: {
            surface:     'rgba(15, 23, 42, 0.90)',
            border:      'rgba(51, 65, 85, 0.60)',
            text:        '#f1f5f9',
            muted:       '#94a3b8',
            today:       'rgba(37, 99, 235, 0.20)',
            todayBorder: '#3b82f6',
            weekend:     'rgba(30, 41, 59, 0.30)',
            accent:      '#6366f1',
          }
        }
      }
    }
  }
</script>
```

Bootstrap is **removed** from `index.html` after Tailwind is confirmed working.

### System Font Stack

No external font loading. Raspberry Pi has no reliable internet access during kiosk operation, and font loading adds latency.

```css
--font-sans: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont,
             "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
--font-mono: ui-monospace, "Cascadia Code", "Source Code Pro", Menlo, monospace;
```

Tailwind config:
```js
theme: {
  extend: {
    fontFamily: {
      sans: ['ui-sans-serif', 'system-ui', '-apple-system', 'BlinkMacSystemFont',
             '"Segoe UI"', 'Roboto', '"Helvetica Neue"', 'Arial', 'sans-serif'],
    }
  }
}
```

### Color Palette

#### Circadian State Gradients

ore oCircadian state boundaries are computed dynamically from astronomical sunrise/sunset (see Section 5.4 Dynamic Circadian Service). The gradients below are applied to each state regardless of the exact time boundaries.

| State | Dynamic Time Window | CSS Gradient |
|-------|---------------------|-------------|
| Dawn | 30 min before sunrise → sunrise | `linear-gradient(180deg, #1a0533 0%, #7b2d8b 40%, #e8834a 80%, #f5c842 100%)` |
| Day | sunrise → sunset | `linear-gradient(180deg, #0f4c8a 0%, #1a7fd4 40%, #5bb8f5 80%, #e8f4fd 100%)` |
| Dusk | sunset → 30 min after sunset | `linear-gradient(180deg, #0d1b2a 0%, #1e3a5f 30%, #c0392b 65%, #e67e22 85%, #f39c12 100%)` |
| Night | 30 min after sunset → 30 min before sunrise (next day) | `linear-gradient(180deg, #020408 0%, #0a0f1e 40%, #0d1b2a 100%)` |

#### Weather Overlay CSS Keyframe Animations

Weather overlays are applied as a second `position: fixed; inset: 0` layer between the gradient and the legibility layer. They use CSS `opacity` and `transform` only — no `backdrop-filter`.

| Condition | CSS Class | Animation Description |
|-----------|-----------|----------------------|
| Clear | `.weather-clear` | No overlay (transparent) |
| Cloudy | `.weather-cloudy` | Slow-drifting semi-transparent grey ellipses, `opacity: 0.25` |
| LightRain | `.weather-light-rain` | Diagonal streaks via `linear-gradient` + `animation: rain-fall 0.8s linear infinite`; subtle `opacity: 0.35` pulse |
| HeavyRain | `.weather-heavy-rain` | Diagonal streaks via `linear-gradient` + `animation: rain-fall 0.5s linear infinite`; stronger `opacity: 0.65` pulse + slight `filter: blur(1px)` on overlay element |
| Thunder | `.weather-thunder` | Occasional full-screen white flash: `animation: thunder-flash 8s ease-in-out infinite` |
| Snow | `.weather-snow` | Drifting white dots via CSS `radial-gradient` + `animation: snow-drift 6s linear infinite` |
| WindMist | `.weather-windmist` | Fast horizontal blur streaks, `opacity: 0.15`, `animation: mist-sweep 1.2s linear infinite` |

All weather animations use `will-change: transform` on the overlay element. The `AnimatedBackground` user preference disables weather animations (sets overlay to `display: none`) while retaining the circadian gradient.

#### Legibility Layer

```css
.legibility-layer {
  background-color: rgba(2, 6, 23, 0.85); /* slate-950 at 85% */
  position: absolute;
  inset: 0;
  z-index: 5;
}
```

The main UI content sits at `z-index: 10` above this layer.

#### UI Color Tokens (Tailwind custom theme)

```js
colors: {
  kiosk: {
    surface:    'rgba(15, 23, 42, 0.90)',   // slate-900/90 — card/panel backgrounds
    border:     'rgba(51, 65, 85, 0.60)',   // slate-700/60 — borders
    text:       '#f1f5f9',                  // slate-100 — primary text
    muted:      '#94a3b8',                  // slate-400 — secondary text
    today:      'rgba(37, 99, 235, 0.20)',  // blue-600/20 — today row highlight
    todayBorder:'#3b82f6',                  // blue-500 — today left border
    weekend:    'rgba(30, 41, 59, 0.30)',   // slate-800/30 — weekend row
    accent:     '#6366f1',                  // indigo-500 — primary accent
  }
}
```

### Spacing / Sizing System

| Token | Value | Usage |
|-------|-------|-------|
| Touch target minimum | 64 × 64 px | All buttons, event tiles, nav controls |
| Header height | 120 px | `DashboardHeader` |
| View area height | 1800 px | `1920 - 120` |
| Month row height | ~55 px | `(1800 - 32) / 31 ≈ 57px` (32px for padding) |
| Time axis width | 72 px | Left column in Today/Week views |
| Calendar column min-width | 160 px | Per-calendar column in Today/Week views |
| Font size — event title | 1rem (16px) | Readable at 2m distance |
| Font size — time label | 0.875rem (14px) | Time axis labels |
| Font size — header | 1.5rem (24px) | Month/year display |

### Burn-In Protection

`BurnInOrbit.razor` applies a CSS `transform: translate(Xpx, Ypx)` to the entire `#app` root element. Every 120 seconds, the offset cycles through a 1–2px orbit pattern:

```
Orbit sequence (px offsets from origin):
(0,0) → (1,0) → (1,1) → (0,1) → (-1,1) → (-1,0) → (-1,-1) → (0,-1) → (1,-1) → (0,0)
```

The transition uses `transition: transform 2s ease-in-out` so the shift is imperceptible during normal use. The orbit is implemented as a `System.Threading.Timer` in the component's `OnInitializedAsync`, calling `StateHasChanged` on each tick.

---

## 4. Component Specifications

### 4.1 DashboardHeader

**File**: `src/FamilyHQ.WebUi/Components/DashboardHeader.razor`

**Height**: 120px fixed. Full-width. `z-index: 20` (above legibility layer).

**Layout** (left to right):

```
[◄ Year ►]  [◄ Month Name ►]  [Today] [Week] [Month]  [☁ Weather]  [⚙]
```

**Parameters**:
```csharp
[Parameter] public int Year { get; set; }
[Parameter] public int Month { get; set; }
[Parameter] public DashboardView ActiveView { get; set; }
[Parameter] public WeatherState? Weather { get; set; }
[Parameter] public EventCallback<int> OnYearChanged { get; set; }
[Parameter] public EventCallback<int> OnMonthChanged { get; set; }
[Parameter] public EventCallback<(int Year, int Month)> OnDateJumped { get; set; }
[Parameter] public EventCallback<DashboardView> OnViewChanged { get; set; }
[Parameter] public EventCallback OnSettingsClicked { get; set; }
```

**Year Navigation**: `◄` and `►` buttons, each 64 × 64 px. Clicking `◄` emits `OnYearChanged(Year - 1)`. Clicking `►` emits `OnYearChanged(Year + 1)`.

**Month Navigation**: `◄` and `►` buttons, each 64 × 64 px. Clicking the month name text (minimum 120px wide touch target) opens `MonthQuickJumpModal`. Clicking `◄` emits `OnMonthChanged(Month - 1)` (wraps year). Clicking `►` emits `OnMonthChanged(Month + 1)` (wraps year).

**View Switcher**: Three pill buttons — "Today", "Week", "Month". Active state: `bg-kiosk-accent text-white`. Inactive: `bg-kiosk-surface text-kiosk-muted`. Each button minimum 80 × 64 px.

**Weather Indicator**: Displays a weather icon (CSS-only SVG or Unicode symbol) and temperature. Tapping opens a weather detail tooltip. If `Weather` is null, shows a subtle loading indicator.

**Settings Gear**: 64 × 64 px button, fixed right. Emits `OnSettingsClicked`.

### 4.2 MonthQuickJumpModal

**File**: `src/FamilyHQ.WebUi/Components/MonthQuickJumpModal.razor`

Full-screen overlay (`position: fixed; inset: 0; z-index: 100`). Displays:
- Year with `◄` / `►` navigation (internal state, starts at current year)
- 4 × 3 grid of month buttons (Jan–Dec), each 200 × 80 px
- Currently selected month highlighted with `bg-kiosk-accent`
- Tapping a month emits `OnDateSelected(year, month)` and closes

### 4.3 TodayView

**File**: `src/FamilyHQ.WebUi/Components/TodayView.razor`

**Purpose**: 24-hour vertical time-blocked grid for a single day. Columns = family member calendars.

**Layout**:
```
┌──────┬──────────┬──────────┬──────────┐
│ Time │ Alice    │ Bob      │ Family   │  ← sticky header row
├──────┼──────────┼──────────┼──────────┤
│ All  │ [event]  │          │ [event]  │  ← all-day row
│ Day  │          │          │          │
├──────┼──────────┼──────────┼──────────┤
│ 00   │          │          │          │
│ 01   │          │          │          │
│ ...  │          │          │          │
│ 23   │          │          │          │
└──────┴──────────┴──────────┴──────────┘
```

**Parameters**:
```csharp
[Parameter] public DateOnly Date { get; set; }
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; }
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; }
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId, TimeOnly Time)> OnSlotTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
```

**Time Axis**: 72px wide left column. Hour labels at each hour boundary. Labels are right-aligned, `text-kiosk-muted`, 14px.

**Event Tiles**:
- Positioned absolutely within their calendar column
- `top` = `(startMinuteOfDay / 1440) * 100%`
- `height` = `(durationMinutes / 1440) * 100%`
- Minimum height: 64px (touch target)
- Background: calendar color from `CalendarSummaryViewModel.Color`
- Content: `Title` (bold), `Start–End` time (14px)
- Overlapping events: split column width 50/50 (up to 2 overlaps); beyond 2, stack with 4px left offset

**Real-Time Red Line**:
- Only shown when `Date == DateOnly.FromDateTime(DateTime.Today)`
- `position: absolute; left: 72px; right: 0; height: 2px; background: #ef4444`
- A 10px red circle at the left end
- Updated every 60 seconds via `System.Threading.Timer`
- `top` = `(currentMinuteOfDay / 1440) * 100%`

**Touch Interaction**:
- Tapping an empty slot emits `OnSlotTapped` with the nearest 15-minute boundary
- Tapping an event tile emits `OnEventTapped`
- Minimum slot tap area: 64px height (15-minute slots are 60px at 1440px total height; the tap handler snaps to the nearest 15-min boundary)

**Total height**: 1800px - 120px (header row) - 80px (all-day row) = 1600px for the 24-hour body. This gives 66.7px per hour, which is sufficient for readability. The body is `overflow-y: hidden` — no scrolling.

### 4.4 WeekView

**File**: `src/FamilyHQ.WebUi/Components/WeekView.razor`

**Purpose**: 7-day hybrid grid (Mon–Sun). Each day is a column. Within each day column, events are shown as tiles sized by duration.

**Parameters**:
```csharp
[Parameter] public DateOnly WeekStart { get; set; }  // Monday of the week
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; }
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; }
[Parameter] public EventCallback<(DateOnly Date, TimeOnly Time)> OnSlotTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
```

**Layout**:
```
┌──────┬────────┬────────┬────────┬────────┬────────┬────────┬────────┐
│ Time │ Mon 27 │ Tue 28 │ Wed 29 │ Thu 30 │ Fri 31 │ Sat 1  │ Sun 2  │
├──────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┤
│ All  │        │        │        │        │        │        │        │
│ Day  │        │        │        │        │        │        │        │
├──────┼────────┼────────┼────────┼────────┼────────┼────────┼────────┤
│ 00   │        │        │        │        │        │        │        │
│ ...  │        │        │        │        │        │        │        │
│ 23   │        │        │        │        │        │        │        │
└──────┴────────┴────────┴────────┴────────┴────────┴────────┴────────┘
```

**Column width**: `(1080 - 72) / 7 ≈ 144px` per day column. Events within a day column use the same overlap-splitting logic as `TodayView`.

**Today column**: Highlighted with `bg-kiosk-today` background and `border-t-4 border-kiosk-todayBorder`.

**Weekend columns**: `bg-kiosk-weekend` background.

**Event tiles**: Same positioning logic as `TodayView`. Calendar color is used for tile background. Title and start time shown.

**No scrolling**: Total height = 1800px. Same 1600px body for 24 hours.

### 4.5 CalendarGrid (Month View)

**File**: `src/FamilyHQ.WebUi/Components/CalendarGrid.razor`

**Purpose**: 31-row vertical list. Rows = days of the month (1–31). Columns = family member calendars. Must fit entirely within 1800px without scrolling.

**Row height calculation**:
```
Available height = 1800px - header_row(48px) - padding(16px) = 1736px
Row height = floor(1736 / 31) = 56px
```

**Layout**:
```
┌──────────┬──────────┬──────────┬──────────┐
│ Date/Day │ Alice    │ Bob      │ Family   │  ← sticky header (48px)
├──────────┼──────────┼──────────┼──────────┤
│ 1 Mon    │ [event]  │          │ [event]  │  ← 56px row
│ 2 Tue    │          │ [event]  │          │
│ ...      │          │          │          │
│ 31 Wed   │          │          │          │
└──────────┴──────────┴──────────┴──────────┘
```

**Parameters**:
```csharp
[Parameter] public int Year { get; set; }
[Parameter] public int Month { get; set; }
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; }
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; }
[Parameter] public int EventDensity { get; set; } = 2;  // 1–3 from UserPreferences
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId)> OnCellTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId)> OnOverflowTapped { get; set; }
```

**Sticky Date Column**: 96px wide. `position: sticky; left: 0; z-index: 5`. Displays `"d ddd"` format (e.g., `"27 Fri"`). Font: 14px bold for day number, 12px muted for day name.

**Row Highlighting**:
- Today: `bg-kiosk-today border-l-8 border-kiosk-todayBorder`
- Weekend: `bg-kiosk-weekend`
- Days outside the current month (padding rows for months < 31 days): `opacity-30`

**Cell Content**:
- Events shown as compact capsules: `{HH:mm} {Title}` (or just `{Title}` for all-day events)
- Maximum `EventDensity` events shown (1–3, from user preferences)
- Overflow: `+N` badge in `text-kiosk-accent font-bold`. Tapping emits `OnOverflowTapped`.
- Overlapping timed events: split cell width 50/50

**Column width**: `(1080 - 96) / N` where N = number of visible calendars. Minimum 120px per column; if columns overflow, horizontal scroll is permitted on the grid body only (not the sticky date column).

**Event capsule style**:
```
height: 20px; border-radius: 4px; padding: 0 6px;
font-size: 12px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
background: {calendarColor}; color: white;
```

### 4.6 AmbientBackground

**File**: `src/FamilyHQ.WebUi/Components/AmbientBackground.razor`

**Purpose**: Manages the circadian gradient and weather overlay CSS state machine. Renders two `position: fixed; inset: 0` layers below the main UI.

**Parameters**:
```csharp
[Parameter] public WeatherState? Weather { get; set; }
[Parameter] public bool AnimationsEnabled { get; set; } = true;
```

**State Determination**: The component polls `GET /api/circadian/current` every 5 minutes to retrieve the current `CircadianState` (computed server-side by `CircadianStateService` from astronomical sunrise/sunset). The component also accepts the state as a `[Parameter]` for initial render. On a 60-second timer, the component re-evaluates whether the state has changed and calls `StateHasChanged` if needed.

```csharp
// CircadianState is received from the API, not computed locally from clock time
[Parameter] public CircadianState CurrentState { get; set; } = CircadianState.Night;
```

**CSS class applied to gradient layer**: `ambient-dawn`, `ambient-day`, `ambient-dusk`, `ambient-night`

**CSS class applied to weather overlay layer**: `weather-clear`, `weather-cloudy`, `weather-light-rain`, `weather-heavy-rain`, `weather-thunder`, `weather-snow`, `weather-windmist`

**Transition**: `transition: background 3s ease-in-out` on the gradient layer for smooth circadian transitions.

**Rendered HTML**:
```html
<div class="ambient-gradient ambient-{state}" style="will-change: transform;"></div>
<div class="ambient-weather weather-{condition} @(AnimationsEnabled ? "" : "no-animate")"></div>
```

### 4.7 EventModal

**File**: `src/FamilyHQ.WebUi/Components/EventModal.razor`

**Purpose**: Create and edit events. Supports RRULE recurrence picker and multi-calendar assignment.

**Parameters**:
```csharp
[Parameter] public bool IsVisible { get; set; }
[Parameter] public CalendarEventViewModel? EditingEvent { get; set; }  // null = create mode
[Parameter] public DateOnly InitialDate { get; set; }
[Parameter] public TimeOnly InitialTime { get; set; }
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; }
[Parameter] public EventCallback<CreateEventRequest> OnCreate { get; set; }
[Parameter] public EventCallback<(Guid EventId, UpdateEventRequest Request)> OnUpdate { get; set; }
[Parameter] public EventCallback<Guid> OnDelete { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```

**Fields**:
- Title (text input, required)
- All Day toggle (checkbox)
- Start Date + Time (date/time pickers; time hidden when All Day = true)
- End Date + Time (date/time pickers; time hidden when All Day = true)
- Location (text input, optional)
- Description (textarea, optional)
- Calendar assignment (multi-select chip selector using existing `CalendarChipSelector.razor`)
- Recurrence picker (see below)

**Recurrence Picker**:
- Dropdown: None / Daily / Weekly / Monthly / Custom
- "None" = no RRULE
- "Daily" = `RRULE:FREQ=DAILY`
- "Weekly" = `RRULE:FREQ=WEEKLY;BYDAY={selected days}` with day-of-week checkboxes
- "Monthly" = `RRULE:FREQ=MONTHLY`
- "Custom" = free-text RRULE input (for externally-created complex rules)
- When editing an event with an existing RRULE, the picker shows the parsed state; if the RRULE is too complex to parse into the simple picker, it falls back to "Custom" with the raw string displayed

**Delete Button**: Only shown in edit mode. Triggers `DeleteConfirmModal`. If the event has a `RecurrenceRule`, triggers `RecurrenceEditPrompt` first.

**Save Button**: In edit mode, if the event has a `RecurrenceRule`, triggers `RecurrenceEditPrompt` before calling `OnUpdate`.

**Touch targets**: All inputs minimum 64px tall. Modal is full-screen on kiosk (`position: fixed; inset: 0; z-index: 200`).

### 4.8 RecurrenceEditPrompt

**File**: `src/FamilyHQ.WebUi/Components/RecurrenceEditPrompt.razor`

**Purpose**: When editing or deleting a recurring event, ask the user whether to affect only this instance, this and all following events, or all events in the series.

**Parameters**:
```csharp
[Parameter] public bool IsVisible { get; set; }
[Parameter] public RecurrenceEditAction Action { get; set; }  // Edit or Delete
[Parameter] public EventCallback<RecurrenceEditScope> OnConfirmed { get; set; }
[Parameter] public EventCallback OnCancelled { get; set; }
```

**Enum `RecurrenceEditScope`**: `ThisInstance`, `ThisAndFollowing`, `AllInSeries`

**UI**: Three large buttons (minimum 200 × 80 px each):
- "This event only" → emits `OnConfirmed(ThisInstance)`
  - Creates an exception instance (`IsRecurrenceException = true`, stores `RecurrenceId`)
  - Google Calendar sync: creates an individual event override (EXDATE or instance override)
- "This and following events" → emits `OnConfirmed(ThisAndFollowing)`
  - Modifies the series from this date forward: updates `UNTIL` in the RRULE of the original series, creates a new series starting from this date
  - Google Calendar sync: splits the Google Calendar series at this date
- "All events in series" → emits `OnConfirmed(AllInSeries)`
  - Modifies the master recurring event
  - Google Calendar sync: updates the master Google Calendar event
- "Cancel" → emits `OnCancelled`

**Delete behaviour**: When `Action == RecurrenceEditAction.Delete`, the same three options apply:
- "This event only" → deletes only this instance (adds EXDATE to the series)
- "This and following events" → truncates the series at this date (updates `UNTIL` in RRULE)
- "All events in series" → deletes the entire series master

### 4.9 DeleteConfirmModal

**File**: `src/FamilyHQ.WebUi/Components/DeleteConfirmModal.razor`

**Purpose**: "Are you sure?" confirmation before deletion.

**Parameters**:
```csharp
[Parameter] public bool IsVisible { get; set; }
[Parameter] public string EventTitle { get; set; } = string.Empty;
[Parameter] public EventCallback OnConfirmed { get; set; }
[Parameter] public EventCallback OnCancelled { get; set; }
```

**UI**: Centered modal with event title, "Cancel" (secondary) and "Delete" (red/danger) buttons, each minimum 160 × 64 px.

### 4.10 DayDetailModal

**File**: `src/FamilyHQ.WebUi/Components/DayDetailModal.razor`

**Purpose**: Full event list for a specific day and calendar, triggered by the `+N` overflow badge in `CalendarGrid`.

**Parameters**:
```csharp
[Parameter] public bool IsVisible { get; set; }
[Parameter] public DateOnly Date { get; set; }
[Parameter] public CalendarSummaryViewModel? Calendar { get; set; }
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
[Parameter] public EventCallback<DateOnly> OnCreateTapped { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```

**UI**: Full-screen overlay. Header shows date and calendar name. Scrollable list of all events for that day/calendar. Each event row: title, time, edit/delete actions. "Add Event" button at bottom.

### 4.11 SettingsPanel

**File**: `src/FamilyHQ.WebUi/Components/SettingsPanel.razor`

**Purpose**: User preferences configuration overlay.

**Sections**:
1. **Column Management**: Drag-and-drop reordering of calendar columns (using `@ondragstart`/`@ondrop`). Visibility toggle per calendar.
2. **Color Coding**: Color picker (palette of 12 preset colors) per calendar.
3. **Display Preferences**: Event density (1–3 rows), default view (Today/Week/Month), animated background toggle.
4. **Authentication**: Username display, Sign Out button (fixed `bottom-4 right-4` per spec).

**Persistence**: Changes are saved to `localStorage` immediately on change (optimistic). A debounced `PUT /api/preferences` call syncs to the backend after 1 second of inactivity.

### 4.12 BurnInOrbit

**File**: `src/FamilyHQ.WebUi/Components/BurnInOrbit.razor`

**Purpose**: Applies a 1–2px pixel orbit to the entire `#app` element every 120 seconds to prevent OLED/plasma burn-in.

**Implementation**:
```csharp
private static readonly (int X, int Y)[] OrbitPositions = [
    (0, 0), (1, 0), (1, 1), (0, 1), (-1, 1),
    (-1, 0), (-1, -1), (0, -1), (1, -1)
];
private int _orbitIndex = 0;
private Timer? _timer;

protected override async Task OnInitializedAsync() {
    _timer = new Timer(_ => {
        _orbitIndex = (_orbitIndex + 1) % OrbitPositions.Length;
        InvokeAsync(StateHasChanged);
    }, null, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));
    await JSRuntime.InvokeVoidAsync("setAppTransform",
        OrbitPositions[_orbitIndex].X, OrbitPositions[_orbitIndex].Y);
}
```

JS interop function in `index.html`:
```js
window.setAppTransform = (x, y) => {
    document.getElementById('app').style.transform = `translate(${x}px, ${y}px)`;
    document.getElementById('app').style.transition = 'transform 2s ease-in-out';
};
```

---

## 5. Backend Changes

### 5.1 Weather Service

#### `IWeatherProvider` Interface

**File**: `src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs`

```csharp
namespace FamilyHQ.Core.Interfaces;

public interface IWeatherProvider
{
    /// <summary>
    /// Fetches the current weather state.
    /// Returns null if the provider is unavailable or not configured.
    /// </summary>
    Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken ct = default);
}
```

#### `WeatherCondition` Enum

**File**: `src/FamilyHQ.Core/Models/WeatherCondition.cs`

```csharp
namespace FamilyHQ.Core.Models;

public enum WeatherCondition
{
    Clear,
    Cloudy,
    LightRain,
    HeavyRain,
    Thunder,
    Snow,
    WindMist
}
```

#### `WeatherState` Record

**File**: `src/FamilyHQ.Core/Models/WeatherState.cs`

```csharp
namespace FamilyHQ.Core.Models;

public record WeatherState(
    WeatherCondition Condition,
    double? TemperatureCelsius,
    DateTimeOffset LastUpdated);
```

#### `WeatherBackgroundService`

**File**: `src/FamilyHQ.Services/Weather/WeatherBackgroundService.cs`

- Implements `BackgroundService`
- Polls `IWeatherProvider` every 10 minutes
- Caches the latest `WeatherState` in a thread-safe field
- On each successful poll, calls `IHubContext<WeatherHub>.Clients.All.SendAsync("WeatherUpdate", weatherStateDto)`
- Exposes `WeatherState? Current { get; }` for the initial HTTP fetch

#### `WeatherHub`

**File**: `src/FamilyHQ.WebApi/Hubs/WeatherHub.cs`

```csharp
namespace FamilyHQ.WebApi.Hubs;

public class WeatherHub : Hub
{
    // Clients connect and receive "WeatherUpdate" messages pushed by WeatherBackgroundService
    // No client-to-server messages needed
}
```

Registered in `Program.cs`:
```csharp
app.MapHub<WeatherHub>("/hubs/weather");
```

#### `WeatherStateDto`

**File**: `src/FamilyHQ.Core/DTOs/WeatherStateDto.cs`

```csharp
namespace FamilyHQ.Core.DTOs;

public record WeatherStateDto(
    string Condition,       // WeatherCondition enum name
    double? TemperatureCelsius,
    DateTimeOffset LastUpdated);
```

### 5.2 RRULE Recurrence

#### Schema Changes to `CalendarEvent`

**File**: `src/FamilyHQ.Core/Models/CalendarEvent.cs` — add three properties:

```csharp
// RRULE string for recurring events (e.g. "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR")
// Null for non-recurring events.
public string? RecurrenceRule { get; set; }

// Google's recurringEventId — links an exception instance to its series master.
// Null for non-recurring events and for series masters.
public string? RecurrenceId { get; set; }

// True when this row is a "this instance only" edit of a recurring series.
public bool IsRecurrenceException { get; set; }
```

#### `IRruleExpander` Interface

**File**: `src/FamilyHQ.Core/Interfaces/IRruleExpander.cs`

```csharp
namespace FamilyHQ.Core.Interfaces;

public interface IRruleExpander
{
    /// <summary>
    /// Expands a recurring event master into concrete instances within [rangeStart, rangeEnd].
    /// Exception instances (IsRecurrenceException = true) override the generated instance
    /// for their specific date.
    /// </summary>
    IReadOnlyList<CalendarEvent> Expand(
        CalendarEvent master,
        IReadOnlyList<CalendarEvent> exceptions,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd);
}
```

#### `RruleExpander` Implementation

**File**: `src/FamilyHQ.Services/Calendar/RruleExpander.cs`

- Uses `Ical.Net` NuGet package (requires approval) to parse RRULE strings
- For each occurrence date generated by `Ical.Net`, creates a synthetic `CalendarEvent` with the master's properties but the occurrence's `Start`/`End`
- Checks the `exceptions` list: if an exception exists for a given occurrence date (matched by `RecurrenceId` and date), substitutes the exception event
- Returns the merged list sorted by `Start`

#### Migration
 e
**File**: `src/FamilyHQ.Data.PostgreSQL/Migrations/{timestamp}_AddRruleFields.cs`

Adds three columns to the `Events` table:
```sql
ALTER TABLE "Events"
  ADD COLUMN "RecurrenceRule" text NULL,
  ADD COLUMN "RecurrenceId" text NULL,
  ADD COLUMN "IsRecurrenceException" boolean NOT NULL DEFAULT false;
```

No backfill required (all existing events are non-recurring).

#### Service Layer Changes

**`ICalendarEventService`** gains two new methods:

```csharp
/// <summary>
/// Updates only this instance of a recurring event.
/// Creates an exception row (IsRecurrenceException = true) in the DB.
/// Calls Google Calendar events.patch with the specific instance's eventId.
/// </summary>
Task<CalendarEvent> UpdateInstanceAsync(
    Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

/// <summary>
/// Deletes only this instance of a recurring event.
/// Calls Google Calendar events.delete on the specific instance.
/// Does not affect the series master row.
/// </summary>
Task DeleteInstanceAsync(Guid eventId, CancellationToken ct = default);
```

**`ICalendarEventService.GetEventsForRangeAsync`** (new method):

```csharp
/// <summary>
/// Returns all events (including expanded recurring instances) within [start, end].
/// Used by Today View and Week View.
/// </summary>
Task<IReadOnlyList<CalendarEvent>> GetEventsForRangeAsync(
    DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);
```

**`CalendarEventService`** changes:
- `CreateAsync`: if `CreateEventRequest` includes a `RecurrenceRule`, stores it on the master event
- `GetEventsForRangeAsync`: fetches master events with `RecurrenceRule != null`, calls `IRruleExpander.Expand`, merges with non-recurring events, returns sorted list
- `UpdateInstanceAsync`: creates exception row, calls Google with instance-specific event ID
- `DeleteInstanceAsync`: calls Google delete on instance, removes exception row if present

#### `CreateEventRequest` Changes

Add optional `RecurrenceRule` field:

```csharp
public record CreateEventRequest(
    IReadOnlyList<Guid> CalendarInfoIds,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? RecurrenceRule);   // NEW — null for non-recurring
```

#### `UpdateEventRequest` Changes

Add optional `RecurrenceRule` field:

```csharp
public record UpdateEventRequest(
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? Description,
    string? RecurrenceRule);   // NEW — null = no change to recurrence
```

### 5.3 User Preferences

#### `UserPreferences` Model

**File**: `src/FamilyHQ.Core/Models/UserPreferences.cs`

```csharp
namespace FamilyHQ.Core.Models;

public class UserPreferences
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;

    // JSON-serialised arrays/objects stored as text columns
    public string? ColumnOrderJson { get; set; }       // Guid[]
    public string? HiddenCalendarIdsJson { get; set; } // Guid[]
    public string? CalendarColorsJson { get; set; }    // Dictionary<Guid, string>

    public int EventDensity { get; set; } = 2;
    public string DefaultView { get; set; } = "Today";
    public bool AnimatedBackground { get; set; } = true;
}
```

#### `UserPreferencesDto`

**File**: `src/FamilyHQ.Core/DTOs/UserPreferencesDto.cs`

```csharp
namespace FamilyHQ.Core.DTOs;

public record UserPreferencesDto(
    IReadOnlyList<Guid> ColumnOrder,
    IReadOnlyList<Guid> HiddenCalendarIds,
    IReadOnlyDictionary<Guid, string> CalendarColors,
    int EventDensity,
    string DefaultView,
    bool AnimatedBackground);
```

#### `PreferencesController`

**File**: `src/FamilyHQ.WebApi/Controllers/PreferencesController.cs`

```
GET  /api/preferences        → returns UserPreferencesDto for current user
PUT  /api/preferences        → accepts UserPreferencesDto, saves, returns updated dto
```

#### Frontend Preferences Service

**File**: `src/FamilyHQ.WebUi/Services/UserPreferencesService.cs`

- On startup: loads from `localStorage` first (instant, no flicker)
- Then fetches `GET /api/preferences` and merges (backend wins on conflict)
- On change: writes to `localStorage` immediately, debounces `PUT /api/preferences` by 1 second
- Exposes `UserPreferencesViewModel` as a reactive property; components subscribe via `OnChange` event

### 5.4 Dynamic Circadian Service

The circadian state boundaries are computed daily from astronomical sunrise/sunset using the NOAA solar algorithm (pure math, no external API). This replaces the previous fixed time boundaries.

#### `KioskOptions` Configuration

**File**: `src/FamilyHQ.Services/Options/KioskOptions.cs`

The kiosk location is stored in `appsettings.json` under `Kiosk:Latitude` and `Kiosk:Longitude`:

```json
{
  "Kiosk": {
    "Latitude": 51.5074,
    "Longitude": -0.1278
  }
}
```

```csharp
namespace FamilyHQ.Services.Options;

public class KioskOptions
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
```

#### `ISolarCalculator` Interface

**File**: `src/FamilyHQ.Core/Interfaces/ISolarCalculator.cs`

```csharp
namespace FamilyHQ.Core.Interfaces;

public interface ISolarCalculator
{
    /// <summary>
    /// Computes sunrise and sunset times for a given date and location using the NOAA solar algorithm.
    /// Returns times as DateTimeOffset in UTC.
    /// </summary>
    (DateTimeOffset Sunrise, DateTimeOffset Sunset) Calculate(DateOnly date, double latitude, double longitude);
}
```

#### `SolarCalculator` Implementation

**File**: `src/FamilyHQ.Services/Circadian/SolarCalculator.cs`

- Implements `ISolarCalculator`
- Uses the NOAA solar algorithm (pure math, no external API calls)
- Inputs: date, latitude, longitude
- Outputs: sunrise and sunset as `DateTimeOffset` in UTC

#### `CircadianBoundaries` Model

**File**: `src/FamilyHQ.Core/Models/CircadianBoundaries.cs`

```csharp
namespace FamilyHQ.Core.Models;

public class CircadianBoundaries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public DateTimeOffset Sunrise { get; set; }
    public DateTimeOffset Sunset { get; set; }
    public DateTimeOffset DawnStart { get; set; }   // 30 min before Sunrise
    public DateTimeOffset DuskEnd { get; set; }     // 30 min after Sunset
}
```

Stored in a new `CircadianBoundaries` DB table (EF config: `src/FamilyHQ.Data/Configurations/CircadianBoundariesConfiguration.cs`).

#### `CircadianStateService` Background Service

**File**: `src/FamilyHQ.Services/Circadian/CircadianStateService.cs`

- Implements `BackgroundService`
- Runs daily at midnight to compute and store that day's circadian boundaries in the `CircadianBoundaries` table
- Uses `ISolarCalculator` with the configured `KioskOptions.Latitude` / `KioskOptions.Longitude`
- Exposes `CircadianState GetCurrentState()` — computes the current state from the stored boundaries for today:
  - **Dawn**: `DawnStart` ≤ now < `Sunrise`
  - **Day**: `Sunrise` ≤ now < `Sunset`
  - **Dusk**: `Sunset` ≤ now < `DuskEnd`
  - **Night**: all other times

#### `CircadianController`

**File**: `src/FamilyHQ.WebApi/Controllers/CircadianController.cs`

```
GET /api/circadian/current  → returns { "state": "Day" | "Dawn" | "Dusk" | "Night" }
```

The `AmbientBackground.razor` component polls this endpoint every 5 minutes to update the circadian CSS class.

### 5.5 Simulator Weather Mock Endpoint

The `FamilyHQ.Simulator` (`tools/FamilyHQ.Simulator/`) must be extended with a mock weather endpoint to support E2E testing of weather-driven UI behaviour.

#### New Endpoints

**File**: `tools/FamilyHQ.Simulator/Controllers/WeatherController.cs`

| Method | Route | Purpose |
|--------|-------|---------|
| `POST` | `/api/simulator/weather` | Accepts a `WeatherState` payload (condition + temperature) and stores it in memory |
| `GET` | `/api/simulator/weather` | Returns the current mock weather state |

The in-memory state is initialised to `{ Condition: "Clear", TemperatureCelsius: null }` on startup.

```csharp
// POST /api/simulator/weather
public record SetWeatherRequest(string Condition, double? TemperatureCelsius);

// GET /api/simulator/weather
public record WeatherStateResponse(string Condition, double? TemperatureCelsius, DateTimeOffset LastUpdated);
```

#### `SimulatorWeatherProvider`

**File**: `src/FamilyHQ.Services/Weather/SimulatorWeatherProvider.cs`

- Implements `IWeatherProvider`
- Calls `GET /api/simulator/weather` on the simulator base URL
- Used in dev/test environments instead of a real weather API
- Registered conditionally: when `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing`, `SimulatorWeatherProvider` is registered instead of `NullWeatherProvider`

This allows E2E tests to:
1. `POST /api/simulator/weather` with a desired condition (e.g. `HeavyRain`)
2. Wait for `WeatherBackgroundService` to poll (or trigger a manual poll via a backdoor endpoint)
3. Assert that `AmbientBackground` applies the correct CSS class (`weather-heavy-rain`)

---

## 6. Data Flow Diagrams

### 6.1 Weather State Flow

```
WeatherBackgroundService (polls every 10 min)
    │
    ▼
IWeatherProvider.GetCurrentWeatherAsync()
    │
    ▼ WeatherState
WeatherBackgroundService._current (cached)
    │
    ├──► IHubContext<WeatherHub>.Clients.All.SendAsync("WeatherUpdate", dto)
    │         │
    │         ▼ SignalR push
    │    SignalRService (WebUi)
    │         │
    │         ▼ WeatherStateDto
    │    Index.razor._weatherState
    │         │
    │         ▼ [Parameter]
    │    DashboardHeader (weather indicator)
    │    AmbientBackground (CSS class update)
    │
    └──► GET /api/weather (initial load on page open)
              │
              ▼ WeatherStateDto
         Index.razor._weatherState (initial value)
```

### 6.2 RRULE Event Expansion Flow

```
GET /api/events/range?start=&end=
    │
    ▼
EventsController.GetRange()
    │
    ▼
ICalendarEventService.GetEventsForRangeAsync(start, end)
    │
    ├── ICalendarRepository.GetNonRecurringEventsAsync(start, end)
    │       → CalendarEvent[] (RecurrenceRule == null)
    │
    └── ICalendarRepository.GetRecurringMastersAsync()
            → CalendarEvent[] (RecurrenceRule != null)
                │
                ▼
            IRruleExpander.Expand(master, exceptions, start, end)
                │
                ▼ synthetic CalendarEvent instances
                │
    Merge + sort by Start
    │
    ▼ IReadOnlyList<CalendarEvent>
EventsController maps → CalendarEventDto[]
    │
    ▼ HTTP JSON
CalendarApiService (WebUi) maps → CalendarEventViewModel[]
    │
    ▼ [Parameter]
TodayView / WeekView / CalendarGrid
```

### 6.3 User Preferences Load/Save Flow

```
Page Load
    │
    ├── localStorage.getItem("familyhq:preferences")
    │       │
    │       ▼ (instant, no flicker)
    │   UserPreferencesService._current = localPrefs
    │   Components render with local prefs
    │
    └── GET /api/preferences
            │
            ▼ UserPreferencesDto (backend authoritative)
        UserPreferencesService._current = merge(local, backend)
        localStorage.setItem("familyhq:preferences", merged)
        Components re-render if changed

User Changes Preference
    │
    ├── UserPreferencesService._current = newValue
    ├── localStorage.setItem("familyhq:preferences", newValue)  ← immediate
    ├── Components re-render immediately
    └── debounce(1s) → PUT /api/preferences (newValue)
```

---

## 7. Files Changed Summary

### New Files

| File | Project |
|------|---------|
| `Components/DashboardHeader.razor` | WebUi |
| `Components/TodayView.razor` | WebUi |
| `Components/WeekView.razor` | WebUi |
| `Components/CalendarGrid.razor` | WebUi |
| `Components/AmbientBackground.razor` | WebUi |
| `Components/EventModal.razor` | WebUi |
| `Components/RecurrenceEditPrompt.razor` | WebUi |
| `Components/DeleteConfirmModal.razor` | WebUi |
| `Components/DayDetailModal.razor` | WebUi |
| `Components/MonthQuickJumpModal.razor` | WebUi |
| `Components/SettingsPanel.razor` | WebUi |
| `Components/BurnInOrbit.razor` | WebUi |
| `Services/UserPreferencesService.cs` | WebUi |
| `ViewModels/UserPreferencesViewModel.cs` | WebUi |
| `ViewModels/WeatherStateViewModel.cs` | WebUi |
| `Core/Interfaces/IWeatherProvider.cs` | Core |
| `Core/Interfaces/IRruleExpander.cs` | Core |
| `Core/Interfaces/IUserPreferencesService.cs` | Core |
| `Core/Interfaces/ISolarCalculator.cs` | Core |
| `Core/Models/WeatherCondition.cs` | Core |
| `Core/Models/WeatherState.cs` | Core |
| `Core/Models/UserPreferences.cs` | Core |
| `Core/Models/CircadianBoundaries.cs` | Core |
| `Core/DTOs/WeatherStateDto.cs` | Core |
| `Core/DTOs/UserPreferencesDto.cs` | Core |
| `Services/Weather/WeatherBackgroundService.cs` | Services |
| `Services/Weather/SimulatorWeatherProvider.cs` | Services |
| `Services/Calendar/RruleExpander.cs` | Services |
| `Services/Circadian/SolarCalculator.cs` | Services |
| `Services/Circadian/CircadianStateService.cs` | Services |
| `Services/Options/KioskOptions.cs` | Services |
| `Services/UserPreferencesService.cs` | Services |
| `WebApi/Hubs/WeatherHub.cs` | WebApi |
| `WebApi/Controllers/PreferencesController.cs` | WebApi |
| `WebApi/Controllers/CircadianController.cs` | WebApi |
| `Data/Configurations/UserPreferencesConfiguration.cs` | Data |
| `Data/Configurations/CircadianBoundariesConfiguration.cs` | Data |
| `Migrations/{timestamp}_AddRruleFields.cs` | Data.PostgreSQL |
| `Migrations/{timestamp}_AddUserPreferences.cs` | Data.PostgreSQL |
| `Migrations/{timestamp}_AddCircadianBoundaries.cs` | Data.PostgreSQL |
| `tailwind.config.js` | WebUi |
| `tools/FamilyHQ.Simulator/Controllers/WeatherController.cs` | Simulator |

### Modified Files

| File | Change |
|------|--------|
| `Pages/Index.razor` | Rebuilt as thin shell; all view logic extracted to components |
| `Layout/MainLayout.razor` | Full-bleed kiosk layout; hosts `BurnInOrbit` |
| `wwwroot/index.html` | Remove Bootstrap; add Tailwind CSS; add `setAppTransform` JS |
| `wwwroot/css/app.css` | Stripped to Blazor error UI only |
| `Core/Models/CalendarEvent.cs` | Add `RecurrenceRule`, `RecurrenceId`, `IsRecurrenceException` |
| `Core/DTOs/CreateEventRequest.cs` | Add `RecurrenceRule` |
| `Core/DTOs/UpdateEventRequest.cs` | Add `RecurrenceRule` |
| `Core/Interfaces/ICalendarEventService.cs` | Add `UpdateInstanceAsync`, `DeleteInstanceAsync`, `UpdateSeriesFromAsync`, `GetEventsForRangeAsync` |
| `Services/Calendar/CalendarEventService.cs` | Implement new interface methods; handle all three recurrence edit scopes |
| `Services/Calendar/CalendarSyncService.cs` | Handle exception instances, series splits, and master updates for Google Calendar sync |
| `WebApi/Controllers/EventsController.cs` | Add `GET /api/events/range` endpoint |
| `WebApi/Program.cs` | Register `WeatherHub`, `WeatherBackgroundService`, `IWeatherProvider`, `IRruleExpander`, `IUserPreferencesService`, `CircadianStateService`, `CircadianController` |
| `Data/FamilyHqDbContext.cs` | Add `DbSet<UserPreferences>`, `DbSet<CircadianBoundaries>` |
| `Data/Configurations/CalendarEventConfiguration.cs` | Add RRULE column configurations |
| `FamilyHQ.WebUi.csproj` | Add Tailwind NuGet package reference |
| `FamilyHQ.Services.csproj` | Add `Ical.Net` NuGet package reference |
| `Directory.Packages.props` | Add Tailwind and Ical.Net package versions |
| `appsettings.json` | Add `Kiosk:Latitude` and `Kiosk:Longitude` configuration |

### Deleted Files

| File | Reason |
|------|--------|
| `Layout/NavMenu.razor` | Navigation moves into `DashboardHeader` |
| `Layout/NavMenu.razor.css` | Deleted with NavMenu |

---

## 8. Design Principles

### Google Calendar First

FamilyHQ is a **display and convenience layer** on top of Google Calendar. Family members will create, edit, and delete events primarily through the Google Calendar app on their phones. FamilyHQ must:

1. **Fully support all changes made in Google Calendar** — recurring event exceptions, series splits, deletions, and edits made in Google must be reflected correctly in FamilyHQ after the next sync.
2. **Write back to Google Calendar correctly** — all three recurrence edit scopes (`ThisInstance`, `ThisAndFollowing`, `AllInSeries`) must produce the correct Google Calendar API calls so that the changes are visible in Google Calendar on users' phones.
3. **Never diverge from Google Calendar state** — the local DB is a cache of Google Calendar data. Sync conflicts are resolved in favour of Google Calendar.

The `FamilyHQ.Simulator` simulates the actual behaviour of the Google Calendar API endpoints that FamilyHQ interacts with. The simulator must faithfully implement the Google Calendar API contract (including RRULE handling, exception instances, and series splits) so that E2E tests exercise the real integration paths.

## 9. Out of Scope

- **Actual weather provider implementation**: `IWeatherProvider` is defined and wired up, but no concrete implementation (e.g., OpenWeatherMap, Met Office) is included. A stub `NullWeatherProvider` that returns `null` is provided for development. `SimulatorWeatherProvider` is used in dev/test environments.
- **Drag-and-drop column reordering**: The `SettingsPanel` spec includes this, but the implementation uses basic up/down buttons if drag-and-drop proves unreliable on touch. Full drag-and-drop is a stretch goal.
- **Week View navigation**: The `DashboardHeader` navigates by month. In Week View, the `◄`/`►` arrows navigate by week instead. The exact UX for this is deferred to implementation.
- **Offline mode**: No service worker or offline caching. The kiosk is assumed to have reliable LAN connectivity.
- **Multi-user / multi-account**: The kiosk operates under a single Google account. Multi-account support is not in scope.
- **Accessibility (WCAG)**: The kiosk is a private family device. WCAG compliance is not a requirement, though reasonable contrast ratios are maintained.

---

## 9. Open Questions

kins cliAll questions resolved. No open questions remain.

| # | Question | Resolution |
|---|----------|------------|
| 1 | Which Tailwind .NET integration package? `Tailwind.AspNetCore` (NuGet) vs a custom MSBuild target calling the Tailwind CLI binary? | **RESOLVED — Tailwind Play CDN.** Add a single `<script src="https://cdn.tailwindcss.com"></script>` tag to `index.html`. No NuGet package, no Node.js build step, no MSBuild target. No changes to `FamilyHQ.WebUi.csproj`. |
| 2 | Which RRULE library? `Ical.Net` adds ~500KB to the WASM bundle. | **RESOLVED — Server-side expansion only.** `Ical.Net` NuGet package is added to `FamilyHQ.Services` (backend only). The backend expands RRULE into concrete instances before sending to the frontend. No WASM bundle impact. |
| 3 | Weather provider: is there a preferred API (OpenWeatherMap, Met Office, wttr.in)? | **RESOLVED — Deferred.** `IWeatherProvider` interface and `NullWeatherProvider` stub are implemented. The concrete provider implementation is TBD and out of scope for this redesign. |
| 4 | Should the `GET /api/events/range` endpoint expand RRULE events server-side or return masters + exceptions for frontend expansion? | **RESOLVED — Server-side only.** The backend expands RRULE into concrete instances. The frontend receives only concrete `CalendarEventDto` objects. No RRULE parsing in WASM. |
| 5 | Column order in Month View: should the sticky date column always be leftmost, or should it be configurable? | **RESOLVED — Always visible and fixed.** The sticky date column is always leftmost and never user-configurable. |
| 6 | For the Week View, when switching from Month navigation mode to Week view, which week is shown? | **RESOLVED — Week containing the currently highlighted/selected date.** The `DashboardHeader` tracks a `SelectedDate`; switching to Week view shows the week that contains that date. |
