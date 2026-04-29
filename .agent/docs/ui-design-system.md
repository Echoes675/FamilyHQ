# UI Design System

FamilyHQ is a portrait-orientation kitchen kiosk running on a Raspberry Pi 3B+. This document is the canonical reference for UI decisions. Read it before making any CSS, layout, or component changes.

## Display & Input Constraints

| Property | Value |
|---|---|
| Device | Raspberry Pi 3B+ |
| Browser | Chromium (kiosk mode) |
| Resolution | 1080 × 1920 (portrait) |
| Input | Touch only — no keyboard/mouse |
| Virtual keyboard | matchbox-keyboard or onboard (system) |
| Min touch target | **48 × 48 px** on all interactive elements |

**Never use:**
- `backdrop-filter: blur()` — crashes GPU on Pi 3B+
- JS animation loops (setInterval/requestAnimationFrame at high frequency)
- Canvas or WebGL rendering
- Heavy CSS filters (`filter: blur()`, `filter: drop-shadow()` on large elements)

**Safe for Pi:**
- CSS `transition` and `animation` on `opacity`, `transform`, `background-color`, `color`
- CSS `@property` registered custom properties (Chromium 85+, fine on current Pi OS)
- CSS `linear-gradient` backgrounds
- SVG icons (inline or `<img>`)

## Virtual Keyboard — Input Visibility

When a user taps an `<input>`, the OS virtual keyboard slides up and covers the lower portion of the screen. To ensure the focused input stays visible:

1. All input fields must live inside a **scrollable container** (`overflow-y: auto`).
2. Add this JS on every `<input>` focus event:
   ```js
   element.scrollIntoView({ behavior: 'smooth', block: 'center' });
   ```
3. Add bottom padding to the scrollable container using the Virtual Keyboard API inset:
   ```css
   .scrollable-container {
     padding-bottom: env(keyboard-inset-height, 0px);
   }
   ```
4. In Blazor, wire this up via JS interop on `@onfocus` of each `InputText`.

This pattern applies to: the Settings page location input, the Event modal, and any future form fields.

### Time inputs on touchscreens

Native `<input type="time">` on Firefox/Linux opens a small spinner that does **not** invoke the OS on-screen keyboard, so it cannot be edited via touch on the kiosk. Use the `TimePicker` Blazor component (`Components/Dashboard/TimePicker.razor`) for any new time field. It provides large +/- step buttons (≥ 48 px touch targets) and a typeable `inputmode="numeric"` text fallback that DOES trigger the OSK. Bind via `Value` / `ValueChanged` on a `TimeOnly`.

## Time-of-Day Theme System

### Periods & Boundaries

Four periods per day. Boundaries are calculated from sunrise/sunset for the user's location (SunCalcNet library) and stored in the `DayTheme` DB record each day.

| Period | Starts at | Description |
|---|---|---|
| **Morning** | Civil dawn | Sky begins to lighten before sunrise |
| **Daytime** | Sunrise | Sun above horizon |
| **Evening** | 1 hour before sunset | Golden hour |
| **Night** | Civil dusk | Sun below horizon, sky dark |

### Colour Palette — Warm & Natural

All four themes are derived from the "Warm & Natural" direction. Text contrast is WCAG AA compliant in all themes.

#### Morning
| Variable | Value | Usage |
|---|---|---|
| `--theme-bg-start` | `#FFF8F0` | Gradient start (top) |
| `--theme-bg-end` | `#FFD8A8` | Gradient end (bottom) |
| `--theme-surface` | `rgba(255,255,255,0.75)` | Card / panel backgrounds |
| `--theme-border` | `rgba(124,58,0,0.15)` | Card borders |
| `--theme-text` | `#3B1800` | Body text |
| `--theme-text-muted` | `#7C3A00` | Secondary / label text |
| `--theme-accent` | `#F97316` | Buttons, active tabs, event pills |
| `--theme-accent-hover` | `#EA580C` | Hover state for accent |
| `--theme-today` | `rgba(249,115,22,0.15)` | Today cell background |
| `--theme-weekend` | `rgba(249,115,22,0.05)` | Weekend cell background |

#### Daytime
| Variable | Value | Usage |
|---|---|---|
| `--theme-bg-start` | `#E8F4FD` | Gradient start |
| `--theme-bg-end` | `#C8E6FA` | Gradient end |
| `--theme-surface` | `rgba(255,255,255,0.80)` | |
| `--theme-border` | `rgba(26,74,110,0.15)` | |
| `--theme-text` | `#0F2A40` | |
| `--theme-text-muted` | `#1A4A6E` | |
| `--theme-accent` | `#3B82F6` | |
| `--theme-accent-hover` | `#2563EB` | |
| `--theme-today` | `rgba(59,130,246,0.15)` | |
| `--theme-weekend` | `rgba(59,130,246,0.05)` | |

#### Evening
| Variable | Value | Usage |
|---|---|---|
| `--theme-bg-start` | `#2D1B4E` | Gradient start |
| `--theme-bg-end` | `#4A2060` | Gradient end |
| `--theme-surface` | `rgba(255,255,255,0.10)` | |
| `--theme-border` | `rgba(244,200,122,0.2)` | |
| `--theme-text` | `#F4E8D0` | |
| `--theme-text-muted` | `#F4C87A` | |
| `--theme-accent` | `#C084FC` | |
| `--theme-accent-hover` | `#A855F7` | |
| `--theme-today` | `rgba(192,132,252,0.2)` | |
| `--theme-weekend` | `rgba(192,132,252,0.08)` | |

#### Night
| Variable | Value | Usage |
|---|---|---|
| `--theme-bg-start` | `#0A0E1A` | Gradient start |
| `--theme-bg-end` | `#0F1628` | Gradient end |
| `--theme-surface` | `rgba(255,255,255,0.06)` | |
| `--theme-border` | `rgba(139,179,232,0.15)` | |
| `--theme-text` | `#E2EAF4` | |
| `--theme-text-muted` | `#8BB3E8` | |
| `--theme-accent` | `#60A5FA` | |
| `--theme-accent-hover` | `#3B82F6` | |
| `--theme-today` | `rgba(96,165,250,0.15)` | |
| `--theme-weekend` | `rgba(96,165,250,0.05)` | |

### Glass Surface System

Bootstrap has been removed entirely. All UI is built with custom CSS in `app.css`. The design language is **glassmorphism-lite**: semi-transparent surfaces with white border glow and layered shadows — no blur effects (Pi 3B+ GPU constraint).

#### Glass CSS Variables (per theme)

Each theme block defines these additional glass surface variables:

| Variable | Purpose |
|---|---|
| `--theme-surface-opacity` | Base opacity multiplier for glass surfaces |
| `--theme-glass-border` | 1px white-tinted border on glass panels |
| `--theme-glass-ring` | Outer glow ring (box-shadow layer 1) |
| `--theme-glass-shadow` | Drop shadow (box-shadow layer 2) |
| `--theme-glass-highlight` | Top edge highlight (box-shadow layer 3) |
| `--theme-glass-divider` | Subtle divider line colour inside panels |
| `--theme-modal-bg` | Solid theme-matched background for modals (not glass) |

Default `--theme-surface-opacity` per theme:
- **Morning**: `0.55`
- **Daytime**: `0.55`
- **Evening**: `0.12`
- **Night**: `0.08`

#### User-Controlled Display Variables

| Variable | Default | Range | Purpose |
|---|---|---|---|
| `--user-surface-multiplier` | `1` | `0`–`2` | Scales surface opacity (set via JS interop from `DisplaySettingService`) |
| `--theme-transition-duration` | `15s` | — | Background gradient transition speed (was 45s) |

`DisplaySettingService` / `IDisplaySettingService` is the Blazor service that applies these at runtime via JS interop (`window.displaySettings.applySettings(multiplier, transitionDuration)`).

#### `.glass-surface` Class

The canonical surface class. All panels, cards, and containers use it. **Modals use `--theme-modal-bg` instead** (solid, theme-matched background for readability):
```css
.glass-surface {
  background: rgba(255,255,255, calc(var(--theme-surface-opacity) * var(--user-surface-multiplier)));
  border: 1px solid var(--theme-glass-border);
  box-shadow:
    0 0 0 1px var(--theme-glass-ring),
    0 4px 24px var(--theme-glass-shadow),
    inset 0 1px 0 var(--theme-glass-highlight);
  color: var(--theme-text);
}
```

No hardcoded hex colours on any component. Always reference theme variables.

#### Shape Language

| Element | Border Radius |
|---|---|
| Cards, panels, modals | `8px` |
| Buttons | `6px` |
| Table cells | `4px` |

#### Typography

**DM Sans** is self-hosted (weights 400 / 500 / 600 / 700). No external font CDN dependency.

### Component Classes

Bootstrap utility classes have been replaced with custom equivalents in `app.css`:

**Buttons**: `.btn-primary`, `.btn-secondary`, `.btn-ghost`, `.btn-danger`

**View tabs**: `.view-tabs` (container), `.view-tab` (individual tab), `.view-tab.active`

**Modals**: `.modal-overlay`, `.modal-container`, `.modal-header`, `.modal-body`, `.modal-footer`

**Forms**: `.form-group`, `.form-label`, `.form-input`, `.form-select`

**Alerts**: `.alert-info`, `.alert-success`, `.alert-warning`, `.alert-error`

**Spinner**: `.spinner`

**Utility layout** (replace Bootstrap grid/flex utilities):
- `.flex`, `.flex-col`, `.items-center`, `.items-start`, `.items-end`
- `.justify-between`, `.justify-center`, `.justify-end`
- `.mb-1` through `.mb-5`, `.mt-1` through `.mt-5`, `.gap-1` through `.gap-4`
- `.w-full`, `.text-center`, `.text-right`

### CSS Architecture

#### Registered custom properties (@property)
Register colour variables as typed `<color>` values so the browser can interpolate them on transition:

```css
@property --theme-bg-start {
  syntax: '<color>';
  inherits: true;
  initial-value: #FFF8F0;
}
/* ... repeat for each colour variable ... */
```

#### Theme blocks
```css
[data-theme="morning"]  { --theme-bg-start: #FFF8F0; --theme-surface-opacity: 0.55; /* ... */ }
[data-theme="daytime"]  { --theme-bg-start: #E8F4FD; --theme-surface-opacity: 0.55; /* ... */ }
[data-theme="evening"]  { --theme-bg-start: #2D1B4E; --theme-surface-opacity: 0.12; /* ... */ }
[data-theme="night"]    { --theme-bg-start: #0A0E1A; --theme-surface-opacity: 0.08; /* ... */ }
```

#### Background layer
```css
#theme-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  background: linear-gradient(160deg, var(--theme-bg-start), var(--theme-bg-end));
  transition:
    --theme-bg-start var(--theme-transition-duration) ease-in-out,
    --theme-bg-end   var(--theme-transition-duration) ease-in-out;
  pointer-events: none;
}
```

#### Transition on text/accent colours
```css
body {
  transition: color var(--theme-transition-duration) ease-in-out;
  color: var(--theme-text);
}
.accent-element {
  transition: background-color var(--theme-transition-duration) ease-in-out, color var(--theme-transition-duration) ease-in-out;
}
```

### Theme Switching — Blazor Flow

1. On page load, `ThemeService.InitialiseAsync()` calls `GET /api/daytheme/today`, derives the current period, and calls JS interop `theme.setTheme(period)`.
2. `SignalRService.OnThemeChanged` fires when the server pushes a `ThemeChanged` message. `ThemeService` calls `theme.setTheme(newPeriod)`.
3. `theme.js` sets `document.body.setAttribute('data-theme', period.toLowerCase())`.
4. CSS takes over — `@property` transitions interpolate all colours over the configured transition duration with no further JS involvement.
5. `DisplaySettingService.InitialiseAsync()` calls `GET /api/settings/display` and applies `--user-surface-multiplier` and `--theme-transition-duration` via JS interop.

### Adding a New Themed Component

1. Use only CSS custom properties from the table above — no hardcoded colours.
2. Surface background → `.glass-surface` class (handles background, border, and box-shadow automatically)
3. Body text → `var(--theme-text)`
4. Labels / secondary text → `var(--theme-text-muted)`
5. Interactive / highlighted elements → `var(--theme-accent)` / `var(--theme-accent-hover)`
6. Add `transition: background-color var(--theme-transition-duration) ease-in-out, color var(--theme-transition-duration) ease-in-out` if the element is always visible (not inside a modal that opens/closes).
7. Border radius: `8px` for cards/panels, `6px` for buttons, `4px` for cells.

## DOM Layer Model

```
<body data-theme="...">
  <div id="theme-bg" />        z-index: 0  — full-bleed gradient (CSS only, pointer-events: none)
  <div id="weather-overlay" /> z-index: 1  — CSS weather animations (managed by WeatherOverlay.razor via weather.js)
  <div id="app">               z-index: 2  — all Blazor content
```

**Never place content inside `#theme-bg` or `#weather-overlay`.** These are pure visual layers.

## Weather Overlay

The `#weather-overlay` div displays CSS-only weather animations driven by `WeatherOverlay.razor` via JS interop (`weather.js`). The overlay applies CSS classes to `#weather-overlay` based on the current weather condition:

| CSS Class | Animation |
|---|---|
| `.weather-lightrain` | Moderate falling lines |
| `.weather-heavyrain` | Dense fast falling lines |
| `.weather-drizzle` | Sparse slow falling lines |
| `.weather-thunder` | Heavy rain + periodic lightning flash |
| `.weather-snow` | Falling dots with gentle drift |
| `.weather-sleet` | Mixed rain lines and snow dots |
| `.weather-partlycloudy` | Subtle drifting cloud gradient |
| `.weather-cloudy` | Darker drifting cloud gradient |
| `.weather-fog` | Semi-transparent gradient with opacity pulse |
| `.weather-windy` | Modifier — tilts particle animations ~15° |

Design rules:
- All animations use CSS `@keyframes` on `transform` and `opacity` only (GPU-compositable).
- `will-change` declarations promote animated pseudo-elements to compositor layers.
- Max ~30 animated pseudo-elements.
- Must be independent of the theme layer — weather overlays the gradient, does not replace it.
- `pointer-events: none` always — touch passes through to the UI.
- State transitions: overlay class is swapped; the `transition: opacity 1s ease` on `#weather-overlay` handles fade.
- **Never place content inside `#weather-overlay`** — CSS pseudo-elements (`::before`, `::after`) handle all visuals.

## Settings Page Layout

Sections in order (top to bottom):
1. **Location** — current location pill (Auto / Saved badge), place name input, Save button
2. **Weather** — link to `/settings/weather` sub-page (enable/disable, temperature unit, poll interval, wind threshold)
3. **Today's Theme Schedule** — 4 period tiles showing boundary times
4. **Display** — transparency slider (`--user-surface-multiplier`, 0–2), opaque toggle (forces multiplier to max), transition speed slider (`--theme-transition-duration`). Changes applied in real time via `DisplaySettingService`.
5. **Account** — avatar initials, display name, email, Sign Out button *(at bottom — used infrequently)*

Header contains only: brand name + back arrow. User name is **not** shown in the header.
