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
[data-theme="morning"]  { --theme-bg-start: #FFF8F0; /* ... */ }
[data-theme="daytime"]  { --theme-bg-start: #E8F4FD; /* ... */ }
[data-theme="evening"]  { --theme-bg-start: #2D1B4E; /* ... */ }
[data-theme="night"]    { --theme-bg-start: #0A0E1A; /* ... */ }
```

#### Background layer
```css
#theme-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  background: linear-gradient(160deg, var(--theme-bg-start), var(--theme-bg-end));
  transition:
    --theme-bg-start 45s ease-in-out,
    --theme-bg-end   45s ease-in-out;
  pointer-events: none;
}
```

#### Surface components
All panels, cards, modals, and the day-view container use:
```css
background: var(--theme-surface);
border: 1px solid var(--theme-border);
color: var(--theme-text);
```
No hardcoded hex colours on any component. Always reference theme variables.

#### Transition on text/accent colours
```css
body {
  transition: color 45s ease-in-out;
  color: var(--theme-text);
}
.accent-element {
  transition: background-color 45s ease-in-out, color 45s ease-in-out;
}
```

### Theme Switching — Blazor Flow

1. On page load, `ThemeService.InitialiseAsync()` calls `GET /api/daytheme/today`, derives the current period, and calls JS interop `theme.setTheme(period)`.
2. `SignalRService.OnThemeChanged` fires when the server pushes a `ThemeChanged` message. `ThemeService` calls `theme.setTheme(newPeriod)`.
3. `theme.js` sets `document.body.setAttribute('data-theme', period.toLowerCase())`.
4. CSS takes over — `@property` transitions interpolate all colours over 45 seconds with no further JS involvement.

### Adding a New Themed Component

1. Use only CSS custom properties from the table above — no hardcoded colours.
2. Background → `var(--theme-surface)`
3. Borders → `var(--theme-border)`
4. Body text → `var(--theme-text)`
5. Labels / secondary text → `var(--theme-text-muted)`
6. Interactive / highlighted elements → `var(--theme-accent)` / `var(--theme-accent-hover)`
7. Add `transition: background-color 45s ease-in-out, color 45s ease-in-out` if the element is always visible (not inside a modal that opens/closes).

## DOM Layer Model

```
<body data-theme="...">
  <div id="theme-bg" />        z-index: 0  — full-bleed gradient (CSS only, pointer-events: none)
  <div id="weather-overlay" /> z-index: 1  — reserved for future weather animations (empty now)
  <div id="app">               z-index: 2  — all Blazor content
```

**Never place content inside `#theme-bg` or `#weather-overlay`.** These are pure visual layers.

## Weather Overlay — Extension Point

The `#weather-overlay` div is intentionally empty. Future weather integration will inject CSS animations or lightweight SVG animations into it. Design rules for that future work:

- Animations must be CSS-only or minimal JS (no canvas/WebGL).
- Must be independent of the theme layer — weather overlays the gradient, does not replace it.
- Must use `pointer-events: none` so touch passes through to the UI.
- Must be removable (clear `#weather-overlay` innerHTML) without affecting the theme.

## Settings Page Layout

Sections in order (top to bottom):
1. **Location** — current location pill (Auto / Saved badge), place name input, Save button
2. **Today's Theme Schedule** — 4 period tiles showing boundary times
3. **Account** — avatar initials, display name, email, Sign Out button *(at bottom — used infrequently)*

Header contains only: brand name + settings gear icon. User name is **not** shown in the header.
