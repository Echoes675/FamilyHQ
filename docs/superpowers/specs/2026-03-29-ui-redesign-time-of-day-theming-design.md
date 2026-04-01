# UI Redesign & Time-of-Day Theming — Design Spec

**Date**: 2026-03-29
**Status**: Approved

---

## Overview

A full UI redesign of FamilyHQ replacing the plain Bootstrap look with a professional, ambient design system that reflects the time of day. The app runs as a touch-only kitchen kiosk on a Raspberry Pi 3B+ with a 27" portrait touchscreen. All existing behaviour (Month view, Day view, Agenda view, event interactions) is preserved exactly. Only the visual layer changes.

---

## Goals

1. Four time-of-day colour themes (Morning, Daytime, Evening, Night) that transition slowly over 45 seconds, like a real sunrise/sunset.
2. Theme boundaries calculated from actual sunrise/sunset for the user's location, computed once per day by the API and persisted to the database.
3. SignalR notifies the UI when to transition; the client also derives the correct theme on first load without waiting for a push.
4. All text remains WCAG AA readable in every theme.
5. All pages and components adopt the new look.
6. The design is architecturally extensible for a future weather overlay layer.
7. A Settings page lets the user configure their location by place name.

---

## Non-Goals

- Weather integration (reserved for a future feature; extension point is specified here).
- Changing any calendar data loading, event editing, or view behaviour.
- Supporting multiple simultaneous users or per-user themes.

---

## Deployment Context

| Property | Value |
|---|---|
| Device | Raspberry Pi 3B+ |
| Browser | Chromium kiosk mode (`--kiosk --touch-events=enabled`) |
| Display | 27" 1080p portrait (1080 × 1920) |
| Input | Touch only — no keyboard/mouse |
| Virtual keyboard | `matchbox-keyboard` or `onboard` (system-level) |
| WebApi | Separate web server; Pi accesses over LAN |

---

## Colour System — Warm & Natural

Four themes. All surface colours are semi-transparent to let the gradient background show through. Full hex reference is in `.agent/docs/ui-design-system.md`.

| Theme | Background gradient | Accent | Text |
|---|---|---|---|
| Morning | Soft peach → warm amber | `#F97316` orange | `#3B1800` dark brown |
| Daytime | Sky blue → pale blue | `#3B82F6` blue | `#0F2A40` deep navy |
| Evening | Deep violet → dark plum | `#C084FC` purple | `#F4E8D0` cream |
| Night | Near-black navy → dark blue | `#60A5FA` light blue | `#E2EAF4` near-white |

---

## Time-of-Day Period Boundaries

| Period | Boundary | Definition |
|---|---|---|
| Morning | Civil dawn | Sun 6° below horizon — sky begins to lighten |
| Daytime | Sunrise | Sun crosses the horizon |
| Evening | 1 hour before sunset | Golden hour begins |
| Night | Civil dusk | Sun 6° below horizon — sky darkens |

Calculated using the **SunCalcNet** NuGet package with the effective lat/lon for the day. Stored in the `DayTheme` entity.

---

## Data Model

### DayTheme (new entity)
```
Id            int           PK
Date          DateOnly      unique index
MorningStart  TimeOnly      civil dawn
DaytimeStart  TimeOnly      sunrise
EveningStart  TimeOnly      1 hour before sunset
NightStart    TimeOnly      civil dusk
```

### LocationSetting (new entity)
```
Id          int       PK
PlaceName   string    display name (e.g. "Edinburgh, Scotland")
Latitude    double
Longitude   double
UpdatedAt   DateTimeOffset
```
A maximum of one row. When absent, the API falls back to IP-based geolocation.

---

## New Services

### ISunCalculatorService / SunCalculatorService
- Dependency: SunCalcNet NuGet package.
- `CalculateBoundariesAsync(double lat, double lon, DateOnly date) → DayThemeBoundaries`
- Returns the four TimeOnly boundary values for the given date and location.

### ILocationService / LocationService
- `GetEffectiveLocationAsync() → LocationResult`
- Returns saved `LocationSetting` from DB if present.
- Otherwise calls `ip-api.com` (free, no key) to get approximate lat/lon and reverse-geocodes a place name string. Result is **not** persisted — auto-detection is always live.

### IGeocodingService / GeocodingService
- `GeocodeAsync(string placeName) → (double Lat, double Lon)`
- HTTP call to Nominatim OSM API (`https://nominatim.openstreetmap.org/search`). No API key required.
- Used when saving a user-specified location.

### IDayThemeService / DayThemeService
- `EnsureTodayAsync(CancellationToken ct)` — idempotent: calculates and persists today's `DayTheme` if not already present.
- `RecalculateForTodayAsync(CancellationToken ct)` — force-overwrites today's `DayTheme` using the current effective location. Called after a location change.
- `GetTodayAsync(CancellationToken ct) → DayThemeDto` — returns today's boundaries + current period.

### DayThemeSchedulerService (IHostedService)
- On startup: calls `DayThemeService.EnsureTodayAsync()`.
- Derives current period from today's boundaries.
- Loop: `Task.Delay` until next boundary time (using an internal `CancellationTokenSource`), then broadcasts `ThemeChanged(periodName)` via `IHubContext<CalendarHub>` to all connected clients, then repeats.
- At midnight: calls `EnsureTodayAsync()` for the new day and recalculates the loop.
- Exposes `TriggerRecalculationAsync()`: cancels the current `Task.Delay` CTS, causing the loop to immediately re-read today's boundaries from the DB and re-derive the current period. Called by the settings controller after a location is saved.

---

## API Changes

### New endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/daytheme/today` | Returns `DayThemeDto` (date, 4 boundary times, current period name) |
| GET | `/api/settings/location` | Returns `LocationSettingDto` or 404 if not set |
| POST | `/api/settings/location` | Body: `{ placeName: string }` → geocodes → saves → recalculates today's theme → pushes `ThemeChanged` → returns `LocationSettingDto` |

### CalendarHub changes
New outbound message pushed by `DayThemeSchedulerService`:
```
ThemeChanged(string period)
```
`period` is one of: `"Morning"`, `"Daytime"`, `"Evening"`, `"Night"`.

---

## Blazor UI Changes

### DOM Layer Model (index.html)
```html
<body data-theme="morning">
  <div id="theme-bg" aria-hidden="true"></div>      <!-- z-index 0: gradient -->
  <div id="weather-overlay" aria-hidden="true"></div> <!-- z-index 1: future weather -->
  <div id="app">...</div>                            <!-- z-index 2: all UI content -->
</body>
```

### CSS Architecture (app.css)
- All colour variables registered via CSS `@property` as typed `<color>` so the browser interpolates them.
- Four `[data-theme="..."]` blocks define the variable values per theme.
- `#theme-bg` uses `linear-gradient(160deg, var(--theme-bg-start), var(--theme-bg-end))` with `transition: --theme-bg-start 45s ease-in-out, --theme-bg-end 45s ease-in-out`.
- All components reference `var(--theme-surface)`, `var(--theme-text)`, `var(--theme-accent)`, etc. — no hardcoded hex colours anywhere.
- Full variable table in `.agent/docs/ui-design-system.md`.

### theme.js (new)
```js
export function setTheme(period) {
  document.body.setAttribute('data-theme', period.toLowerCase());
}
```
Registered in `index.html` as an ES module. Called via Blazor JS interop.

### IThemeService / ThemeService (new Blazor service)
- `InitialiseAsync()`:
  - Calls `GET /api/daytheme/today`.
  - Derives current period from boundary times vs. `DateTime.Now`.
  - Calls `JSRuntime.InvokeVoidAsync("theme.setTheme", period)`.
- Subscribes to `SignalRService.OnThemeChanged` → calls `setTheme` on receipt.

### SignalRService changes
- New event: `public event Action<string>? OnThemeChanged`
- Register handler in constructor: `_hubConnection.On<string>("ThemeChanged", period => OnThemeChanged?.Invoke(period))`

### MainLayout.razor changes
- Remove `NavMenu` sidebar (was unused).
- Inject `IThemeService`; call `ThemeService.InitialiseAsync()` in `OnInitializedAsync`.
- The `<article class="content px-4">` padding is replaced with portrait-optimised layout styles.

### DashboardHeader.razor changes
- Remove username display and sign-out button.
- Add settings gear icon linking to `/settings`.

### Settings page (new — `/settings`)
Sections in order:
1. **Location** — current location pill with Auto/Saved badge, place name text input, Save/Update button.
2. **Today's Theme Schedule** — four period tiles showing boundary times for today. Refreshed after a location save.
3. **Account** — avatar initials, display name, email, Sign Out button.

Input visibility: the settings body is a scrollable container. On `<input>` focus, JS interop calls `element.scrollIntoView({ behavior:'smooth', block:'center' })`. Container has `padding-bottom: env(keyboard-inset-height, 0px)`.

### All existing components
Replace all hardcoded colours and Bootstrap colour utilities with CSS custom property references per the design system. Layout and behaviour unchanged.

---

## Touch & Accessibility

- Minimum touch target: **48 × 48 px** on all interactive elements.
- All text meets WCAG AA contrast ratio in all four themes.
- Inputs use `scrollIntoView` + `env(keyboard-inset-height)` pattern (see above).
- Event modal uses the same keyboard-avoidance pattern.

---

## Weather Overlay Extension Point

`<div id="weather-overlay">` sits between the gradient background and the UI. Future weather animations inject CSS or minimal SVG animations here. Rules:
- `pointer-events: none` always.
- Animations must be CSS-only or very lightweight JS.
- Must not affect `#theme-bg` or `#app`.
- Removed by clearing `innerHTML` — no other cleanup needed.

---

## Out of Scope for This Feature

- Lat/lon in `appsettings.json` (replaced by DB-stored LocationSetting + IP fallback).
- Bootstrap `NavMenu` sidebar (removed — was already unused in the rendered layout).
- Counter and Weather placeholder pages from the default Blazor template (can be removed as a cleanup task).

---

## Behaviour Clarifications

### When does a new location take effect?
Saving a location via `POST /api/settings/location` takes effect **immediately**. The flow is:
1. `LocationSetting` is persisted to DB.
2. `DayThemeService.RecalculateForTodayAsync()` overwrites today's `DayTheme` with boundaries derived from the new location.
3. The current period is derived from the new boundaries and `DateTime.Now`.
4. `ThemeChanged(currentPeriod)` is pushed via SignalR to all clients — the UI transitions immediately.
5. `DayThemeSchedulerService.TriggerRecalculationAsync()` cancels the current delay loop so the scheduler re-reads the new boundaries and recalculates the timing for subsequent transitions.

The Settings UI shows "Takes effect immediately" (no "tomorrow" note).

### IP geolocation fallback
The IP-based fallback calls `ip-api.com` on every request to `ILocationService.GetEffectiveLocationAsync()` when no `LocationSetting` is in the DB. The result is never cached or persisted — it is always live. This ensures the fallback stays accurate if the device moves networks, without requiring a DB write.
