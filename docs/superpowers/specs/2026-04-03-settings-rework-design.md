# Settings Rework Design

**Date:** 2026-04-03  
**Branch:** feature/settings-rework  
**Status:** Approved

---

## Overview

Rework the settings experience from a single long-scroll page into a tabbed layout with a vertical tab strip on the left and a content area on the right. All settings are scoped per authenticated user in the database. The settings cog is hidden on the login page — it only appears when the user is authenticated.

---

## Layout

The `/settings` page is restructured as:

```
┌─────────────────────────────────────────────┐
│ ← Settings                (header)          │
├──────────┬──────────────────────────────────┤
│ 👤       │                                  │
│ General  │   <active tab content>           │
│          │                                  │
│ 📍       │                                  │
│ Location │                                  │
│          │                                  │
│ 🌤       │                                  │
│ Weather  │                                  │
│          │                                  │
│ 🖥       │                                  │
│ Display  │                                  │
└──────────┴──────────────────────────────────┘
```

- Tab strip: fixed width (~100px), vertical, left side. Active tab has accent colour highlight + right-edge indicator bar.
- Content area: fills remaining width, scrollable vertically.
- Minimum touch target: 48×48px on all tab items and interactive controls.

---

## Tabs & Content

### General
- Avatar circle showing user initials (derived from username).
- Username displayed.
- **Sign Out** button.

### Location
- **Current location** — shows the active location (name + badge: "Auto" or "Saved"). When auto-detected, the resolved location name is shown so the user can decide whether to accept or override it.
- **Override location** input — place name text field + Save button.
- Reset to auto-detect button (visible only when a saved location exists).
- Hint: "Takes effect immediately."

### Weather
- Enable weather display toggle.
- Temperature Unit selector (Celsius / Fahrenheit).
- Poll Interval (minutes) number input.
- Wind Alert Threshold (km/h) number input.
- Save / Cancel buttons (appear when changes are pending).
- The `/settings/weather` sub-page is **removed** — its content is fully replaced by this tab.

### Display
Two subsections within one tab:

#### Appearance subsection
- **Surface Opacity** slider — range 0–100% (mapped to `SurfaceMultiplier` 0.0–1.0). Replaces the previous 0–200% range. The stored `SurfaceMultiplier` column changes from a 0–2.0 scale to 0–1.0; existing values are rescaled by halving during migration.
- **Opaque surfaces** checkbox — shortcut that locks slider at 100%; unchecking restores the previous slider value. Writes to the existing `OpaqueSurfaces bool` column (no schema change). Does not change the stored `SurfaceMultiplier` value.

#### Theme subsection
- **Auto-change with time of day** toggle — when ON, the theme transitions automatically at each period boundary.
- 4 theme tiles in a 2×2 grid. Each tile uses its own theme's actual colour palette (gradients from the design system):
  - Morning: `#FFF8F0 → #FFD8A8`, text `#3B1800`
  - Daytime: `#E8F4FD → #C8E6FA`, text `#0F2A40`
  - Evening: `#2D1B4E → #4A2060`, text `#F4E8D0`/`#F4C87A`
  - Night: `#0A0E1A → #0F1628`, text `#E2EAF4`/`#8BB3E8`
- Each tile shows the period name and today's calculated boundary time.
- When auto-change is **ON**: tiles are read-only (informational).
- When auto-change is **OFF**: tiles are tappable — selecting one applies that theme instantly site-wide. The selected tile shows a tick mark.
- **Theme Transition Speed** slider (0–60s, step 5s) — enabled only when auto-change is ON; disabled (greyed out) when OFF.

---

## Settings Cog Visibility

The settings cog in `DashboardHeader` is already only rendered inside the `_isAuthenticated` block of `Index.razor`. No structural change is needed — however the component should be reviewed to ensure it cannot be rendered unauthenticated. If the header is ever used outside the auth guard, it must inject `IAuthenticationService` and conditionally render the cog.

---

## Data Model Changes

### New: `ThemeSelection` on `DisplaySetting`

Add a `ThemeSelection` string column to `DisplaySetting`:
- `"auto"` — auto-change is enabled.
- `"morning"` | `"daytime"` | `"evening"` | `"night"` — manual selection, auto-change disabled.
- Default: `"auto"`.

This replaces the need for a separate boolean — the auto toggle state is implied by the value.

### Per-User Scoping

All three settings entities are scoped per `UserId`:

| Entity | Current state | Change |
|---|---|---|
| `LocationSetting` | Already per-user | No change |
| `DisplaySetting` | Global single row | Add `UserId` column; migrate to per-user |
| `WeatherSetting` | Global single row | Add `UserId` column; migrate to per-user |

Repository interfaces (`IDisplaySettingRepository`, `IWeatherSettingRepository`) updated to accept `userId` on all read/write operations.

API endpoints for display and weather settings are updated to use `ICurrentUserService.UserId` (removing `[AllowAnonymous]`).

### WeatherPollerService

The background poller cannot target a single user's settings. Behaviour:
- Queries all users' `WeatherSetting` rows.
- Polls at the **shortest active `PollIntervalMinutes`** across all users who have `Enabled = true`.
- Falls back to a hardcoded default (30 minutes) when no users have weather enabled or no settings exist.

Temperature conversion and wind threshold are applied per-user only when serving API responses — not during background data fetch.

---

## Component Structure (Option B)

`Settings.razor` becomes a thin shell managing only tab state. Each tab is an independent Razor component:

```
Pages/
  Settings.razor                  ← shell: tab strip + @switch → child component
Components/
  Settings/
    SettingsGeneralTab.razor
    SettingsLocationTab.razor
    SettingsWeatherTab.razor
    SettingsDisplayTab.razor
```

`WeatherSettings.razor` (page at `/settings/weather`) is deleted.

---

## API Changes

| Endpoint | Change |
|---|---|
| `GET /api/settings/display` | Remove `[AllowAnonymous]`; scope to `currentUser.UserId` |
| `PUT /api/settings/display` | Remove `[AllowAnonymous]`; scope to `currentUser.UserId`; accept `ThemeSelection` |
| `GET /api/settings/weather` | Remove `[AllowAnonymous]`; scope to `currentUser.UserId` |
| `PUT /api/settings/weather` | Remove `[AllowAnonymous]`; scope to `currentUser.UserId` |

### Theme selection
`ThemeSelection` is included in `PUT /api/settings/display`. No new endpoint is added.

---

## DTOs

- `DisplaySettingDto` — add `ThemeSelection` string property.
- `WeatherSettingDto` — no structural change, just per-user scoping.

---

## Theme Application at Runtime

When the settings page loads and auto-change is OFF, the stored `ThemeSelection` value is applied by setting `data-theme` on `<body>` via JS interop — same mechanism as `DayThemeSchedulerService` but triggered from the client.

When the user taps a theme tile (auto OFF), the theme is applied immediately via JS interop and saved to the API.

When auto-change is toggled ON, the current server-side period is fetched (`GET /api/daytheme/today`) and applied.

---

## Theme Enforcement on App Load

`DisplaySettingService.InitialiseAsync()` (called on startup) loads the user's `DisplaySetting`. If `ThemeSelection != "auto"`, it applies the stored theme immediately and suppresses the SignalR `ThemeChanged` listener until auto is re-enabled.

---

## Migrations

1. `AddUserIdToDisplaySetting` — add nullable `UserId`, backfill any existing row with a default/null, then make non-nullable.
2. `AddUserIdToWeatherSetting` — same pattern.
3. `AddThemeSelectionToDisplaySetting` — add `ThemeSelection varchar(16)` with default `"auto"`.
4. `RescaleSurfaceMultiplier` — update existing `DisplaySetting` rows: `SurfaceMultiplier = SurfaceMultiplier / 2.0` to convert from the old 0–2.0 scale to the new 0–1.0 scale. Also update the `DisplaySettingDtoValidator` max value from `2.0` to `1.0`.

---

## E2E Test Impact

- `Settings.feature` and `SettingsPage.cs` will need updating to navigate via the new tab structure.
- `WeatherSettings.feature` and `WeatherSettingsPage.cs` will be updated to target the Weather tab within `/settings` instead of `/settings/weather`.
- All existing assertions remain valid; only selectors and navigation steps change.

---

## Out of Scope

- Extending the Theme tab with additional theme customisation (noted as future work by the user).
- Multi-user simultaneous sessions (production is single-user).
