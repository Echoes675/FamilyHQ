# Glass Kiosk UI Redesign — Design Spec

**Date**: 2026-03-30
**Status**: Draft
**Prerequisite**: 2026-03-29-ui-redesign-time-of-day-theming-design.md (implemented)

---

## Overview

A component-level visual upgrade of FamilyHQ, replacing Bootstrap with purpose-built custom CSS that uses a "glassmorphism-lite" aesthetic. The existing circadian theme system (4 time-of-day palettes, CSS custom properties, SignalR-driven transitions) is preserved exactly. Only the visual treatment of UI elements changes — buttons, tables, tabs, modals, forms, and panels all get a cohesive glass-surface treatment that works across all four themes.

This spec also adds a "Display" settings section for runtime tuning of surface transparency and theme transition duration, and changes the default transition duration from 45s to 15s.

---

## Goals

1. Remove Bootstrap entirely — no framework CSS, only custom CSS.
2. Restyle all UI components with a glassmorphism-lite surface treatment.
3. Add a self-hosted font family for consistent, distinctive typography.
4. Add user-configurable surface transparency and transition duration in Settings.
5. Maintain all existing behaviour, accessibility, and Pi performance constraints.
6. Preserve the weather overlay extension point and design surfaces to allow weather particles to show through.

## Non-Goals

- Changing any calendar data loading, event editing, or view-switching behaviour.
- Implementing weather animations (future effort; extension point already exists).
- Changing the circadian colour palettes or theme boundary calculations.
- Adding new pages or navigation routes.

---

## Deployment Context

Unchanged from the theming spec. Key constraints that affect this design:

| Property | Value |
|---|---|
| Device | Raspberry Pi 3B+ |
| Browser | Chromium kiosk mode |
| Display | 1080 × 1920 portrait, 27" touchscreen |
| Input | Touch only, 48px minimum touch targets |
| **Forbidden** | `backdrop-filter: blur()`, canvas/WebGL, heavy JS animation loops, `filter: blur/drop-shadow` on large elements |
| **Safe** | CSS `transition`/`animation` on `opacity`, `transform`, `background-color`, `color`; `linear-gradient`; `box-shadow` (static, not animated); `@property` registered custom properties |

---

## Design Language

### Aesthetic Direction: "Glass Kiosk"

A clean, professional interface inspired by Notion/Linear's design discipline combined with subtle glassmorphism depth. The glass effect is achieved without `backdrop-filter` by using semi-transparent `rgba` backgrounds, white border glow, inset top highlights, and layered box-shadows. The result feels modern and elevated without straining the Pi's GPU.

### Shape Language — Slightly Rounded

| Element | Border Radius |
|---|---|
| Cards, panels, modal | `8px` |
| Buttons | `6px` |
| Calendar cells | `4px` |
| Event pills | `6px` |
| Form inputs | `6px` |
| Settings gear icon | `8px` |

### Surface Treatment — Glassmorphism-Lite

Every panel, card, and container surface uses:

```css
background: rgba(255, 255, 255, var(--theme-surface-opacity));
border: 1px solid var(--theme-glass-border);
border-radius: 8px;
box-shadow:
  0 0 0 1px var(--theme-glass-ring),
  0 8px 24px var(--theme-glass-shadow),
  inset 0 1px 0 var(--theme-glass-highlight);
```

No `backdrop-filter`. The transparency allows the gradient background (and future weather animations) to show through the surfaces.

**Performance rule:** Maximum 2 layers of transparency stacked (gradient → card → event item). Never nest a glass surface inside another glass surface.

### New CSS Custom Properties

These extend the existing theme variable set. Each theme block defines values for:

| Variable | Purpose | Morning | Daytime | Evening | Night |
|---|---|---|---|---|---|
| `--theme-surface-opacity` | Surface background opacity (light themes use white base, dark themes use white base at lower opacity) | `0.55` | `0.55` | `0.12` | `0.08` |
| `--theme-glass-border` | Surface border colour | `rgba(255,255,255,0.6)` | `rgba(255,255,255,0.5)` | `rgba(244,200,122,0.15)` | `rgba(139,179,232,0.12)` |
| `--theme-glass-ring` | Outer ring shadow colour | `rgba(124,58,0,0.06)` | `rgba(26,74,110,0.06)` | `rgba(244,200,122,0.06)` | `rgba(139,179,232,0.04)` |
| `--theme-glass-shadow` | Elevation shadow | `rgba(0,0,0,0.04)` | `rgba(0,0,0,0.06)` | `rgba(0,0,0,0.2)` | `rgba(0,0,0,0.3)` |
| `--theme-glass-highlight` | Inset top highlight | `rgba(255,255,255,0.7)` | `rgba(255,255,255,0.6)` | `rgba(255,255,255,0.08)` | `rgba(255,255,255,0.04)` |
| `--theme-glass-divider` | Divider/separator colour | `rgba(124,58,0,0.1)` | `rgba(26,74,110,0.1)` | `rgba(244,200,122,0.1)` | `rgba(139,179,232,0.08)` |
| `--theme-transition-duration` | Theme crossfade time | `15s` | `15s` | `15s` | `15s` |

`--theme-surface-opacity` defaults differ by theme — light themes (morning/daytime) use higher opacity (`0.55`) since the glass effect relies on white transparency over light gradients, while dark themes (evening/night) use lower opacity (`0.08–0.12`) since too much white wash would destroy the dark aesthetic. The Settings slider controls a **user multiplier** (`--user-surface-multiplier`, default `1.0`, range `0.0–2.0`). Each theme computes its effective surface as `rgba(255, 255, 255, calc(var(--theme-surface-opacity) * var(--user-surface-multiplier)))`, clamped to 0–1. This way, moving the slider scales all themes proportionally. When "Opaque surfaces" is toggled on, `--user-surface-multiplier` is ignored and all surfaces use `background: var(--theme-bg-end)` with full opacity (a solid colour from the theme's palette rather than white).

`--theme-transition-duration` replaces the hardcoded `45s` in all transition declarations.

---

## Typography

### Font Choice

A single self-hosted font family with weights 400 (regular), 500 (medium), 600 (semi-bold), and 700 (bold). The specific font will be selected during implementation by prototyping 2-3 options. Candidates should be:

- Legible at arm's length on a 27" display
- Distinctive but not decorative — this is a utility UI, not a marketing page
- Available as WOFF2 with a permissive open-source licence

### Font Loading

- Font files stored in `wwwroot/fonts/`
- Loaded via `@font-face` in `app.css` with `font-display: swap`
- No external CDN dependency
- Fallback stack: `'ChosenFont', system-ui, -apple-system, sans-serif`

### Type Scale

| Usage | Size | Weight | Variable |
|---|---|---|---|
| Brand name (header) | `20px` | 700 | `--theme-text` |
| Month/section headings | `18px` | 600 | `--theme-text` |
| Tab labels | `14px` | 500 (active: 600) | `--theme-text` / `--theme-text-muted` |
| Body text, event names | `14px` | 400 | `--theme-text` |
| Secondary/label text | `13px` | 400 | `--theme-text-muted` |
| Small text (times, badges) | `12px` | 500 | `--theme-text-muted` |
| Calendar day numbers | `14px` | 400 (today: 700) | `--theme-text` |

---

## Component Specifications

### 1. Dashboard Header

Minimal bar at top of page:

```
[FamilyHQ]                          [⚙]
```

- Brand name: left-aligned, 20px bold, `var(--theme-text)`
- Settings gear: 40×40px glass surface button, `border-radius: 8px`, icon in `var(--theme-text-muted)`
- No background on the header bar itself — it floats over the gradient
- Touch target: gear button is 48×48px (with padding)

### 2. View Tabs — Clean Underline

```
Month    Day    Agenda
─────────────────────────   ← full-width 2px border in --theme-glass-divider
▔▔▔▔▔                       ← 2px accent underline on active tab only
```

- Container: `display: flex`, full-width bottom border `2px solid var(--theme-glass-divider)`
- Active tab: `font-weight: 600`, `color: var(--theme-text)`, `border-bottom: 2px solid var(--theme-accent)`, `margin-bottom: -2px`
- Inactive tabs: `font-weight: 500`, `color: var(--theme-text-muted)`, no underline
- Padding: `12px 20px` per tab
- Touch target: full tab area is at least 48px tall
- Transition: underline colour participates in theme transition via `var(--theme-accent)`

### 3. Month Navigation — Ghost Arrows

```
‹    March 2026    [Today]    ›
```

- Prev/Next: bare chevron characters (`‹` / `›`), no background, no border, `color: var(--theme-text-muted)`, `font-size: 20px`
- Touch target: invisible 48×48px padding area around each chevron
- Today button: solid `var(--theme-accent)` background, white text, `border-radius: 6px`, `padding: 8px 20px`, `box-shadow: 0 0 12px color-mix(in srgb, var(--theme-accent) 20%, transparent)`
- Month title: `font-size: 18px`, `font-weight: 600`, `color: var(--theme-text)`
- Layout: `display: flex`, `align-items: center`, `justify-content: space-between`

### 4. Calendar Table (Month View)

The month grid sits inside a glass card:

- **Container:** glass surface (see Surface Treatment above)
- **Day headers (Mon-Sun):** `font-size: 12px`, `font-weight: 600`, `color: var(--theme-text-muted)`, `text-transform: uppercase`
- **Day cells:** `border-radius: 4px`, `padding: 8px`
- **Today cell:** `background: var(--theme-today)`, `box-shadow: inset 0 0 0 2px color-mix(in srgb, var(--theme-accent) 30%, transparent)`, `font-weight: 700`
- **Weekend cells:** `background: var(--theme-weekend)`
- **Previous/next month days:** `opacity: 0.4`
- **Cell borders:** none — use gap spacing (`gap: 2px`) between cells instead of table borders
- **Layout:** CSS Grid `grid-template-columns: repeat(7, 1fr)` instead of `<table>` (or style existing table to look like grid)

### 5. Event Pills (Month/Agenda views)

```
┃ Team standup 09:00
```

- `border-left: 3px solid <calendar-colour>`
- `background: color-mix(in srgb, var(--theme-accent) 12%, transparent)` (or calendar-specific tint)
- `border-radius: 6px`
- `padding: 6px 12px`
- `font-size: 12px`
- Calendar colour dot removed (left border replaces it)
- Overflow indicator: `"+2 more"` as `var(--theme-text-muted)` text below last visible event

### 6. Day View

Structure unchanged (time axis + calendar columns). Visual changes:

- **Container:** glass surface card
- **Sticky header:** glass surface with slightly higher opacity for readability
- **Hour lines:** `1px solid var(--theme-glass-divider)`
- **Current time line:** `2px solid var(--theme-accent)` with a small circle indicator
- **Event blocks:** `background: color-mix(in srgb, <calendar-colour> 20%, var(--theme-surface))`, `border-left: 3px solid <calendar-colour>`, `border-radius: 6px`

### 7. Agenda View

- **Container:** glass surface card
- **Day rows:** alternating subtle opacity difference for readability
- **Today row:** `background: var(--theme-today)`
- **Weekend rows:** `background: var(--theme-weekend)`
- **Event lines:** same left-border style as event pills

### 8. Event Modal

- **Backdrop:** `background: rgba(0, 0, 0, 0.4)` (dark themes: `rgba(0, 0, 0, 0.6)`)
- **Modal surface:** glass treatment, `max-width: 500px`, centered
- **Header:** modal title in `--theme-text`, close button as ghost icon
- **Form labels:** `font-size: 13px`, `font-weight: 500`, `color: var(--theme-text-muted)`
- **Form inputs:** `background: transparent`, `border: 1px solid var(--theme-glass-border)`, `border-radius: 6px`, `color: var(--theme-text)`, `padding: 10px 14px`
- **Input focus state:** `border-color: var(--theme-accent)`, `box-shadow: 0 0 0 2px color-mix(in srgb, var(--theme-accent) 20%, transparent)`
- **Footer buttons:** Primary = solid accent, Secondary = ghost (border only), Destructive = `#ef4444` always (not themed)
- **Date/time inputs:** same styling as text inputs

### 9. Buttons — Full Specification

**Primary button:**
```css
background: var(--theme-accent);
color: var(--theme-on-accent);
border: none;
border-radius: 6px;
padding: 10px 20px;
font-weight: 600;
box-shadow: 0 0 12px color-mix(in srgb, var(--theme-accent) 20%, transparent);
```

**Secondary button (glass):**
```css
background: rgba(255, 255, 255, var(--theme-surface-opacity));
color: var(--theme-text);
border: 1px solid var(--theme-glass-border);
border-radius: 6px;
padding: 10px 20px;
box-shadow: inset 0 1px 0 var(--theme-glass-highlight);
```

**Ghost button (text only):**
```css
background: transparent;
color: var(--theme-text-muted);
border: none;
padding: 10px;
```

**Destructive button:**
```css
background: transparent;
color: #ef4444;
border: 1px solid rgba(239, 68, 68, 0.3);
border-radius: 6px;
padding: 10px 20px;
```
Not themed — red is universal danger signalling.

**All buttons:**
- `min-height: 48px` (touch target)
- `font-size: 14px`
- `cursor: pointer`
- Hover/active: slight brightness shift via `filter: brightness(1.1)` / `brightness(0.95)`
- Transitions participate in theme crossfade via custom properties

### 10. Form Controls

**Text inputs:**
```css
background: transparent;
border: 1px solid var(--theme-glass-border);
border-radius: 6px;
color: var(--theme-text);
padding: 10px 14px;
font-size: 14px;
```

**Focus state:**
```css
border-color: var(--theme-accent);
box-shadow: 0 0 0 2px color-mix(in srgb, var(--theme-accent) 20%, transparent);
outline: none;
```

**Checkboxes:** styled with `accent-color: var(--theme-accent)`

**Select dropdowns:** same border/background treatment as text inputs

---

## Settings Page — "Display" Section

A new section added to the Settings page between "Today's Theme Schedule" and "Account":

### Settings Sections (updated order)

1. **Location** — unchanged
2. **Today's Theme Schedule** — unchanged
3. **Display** — new
4. **Account** — unchanged (bottom)

### Display Section Layout

```
Display
────────────────────────────────────
Surface Transparency
[━━━━━━━━━●━━━━] 100%

☐ Opaque surfaces (disable transparency)

Theme Transition Speed
[━━━━━●━━━━━━━━] 15s
────────────────────────────────────
```

#### Surface Transparency Slider

- Controls `--user-surface-multiplier` which scales the per-theme base opacity
- Range: 0% (fully transparent) to 200% (double opacity)
- Default: 100% (use theme defaults as-is)
- Step: 10%
- Updates the CSS custom property in real-time via JS interop as the user drags
- Value is persisted to a new `DisplaySetting` entity (see Data Model below)
- Label shows current percentage

#### Opaque Surfaces Toggle

- When enabled: sets `--theme-surface-opacity` to `1.0`, hides the transparency slider (or disables it), and simplifies glass effects (removes inset highlight and glass ring shadow)
- Default: off
- Persisted to `DisplaySetting`

#### Theme Transition Speed Slider

- Range: 0s (instant) to 60s
- Default: 15s
- Step: 5s
- Updates `--theme-transition-duration` in real-time via JS interop
- Affects all `transition` declarations that use this variable
- Label shows current value in seconds
- Persisted to `DisplaySetting`

### Slider Touch Requirements

- Slider track: full width of the settings section
- Thumb: minimum 48×48px touch target
- Use native `<input type="range">` styled with CSS for reliability on Chromium/Pi
- Accent colour: `var(--theme-accent)` via `accent-color` property

---

## Data Model Changes

### DisplaySetting (new entity)

```
Id                          int             PK
SurfaceMultiplier           double          0.0–2.0, default 1.0
OpaqueSurfaces              bool            default false
TransitionDurationSecs      int             0–60, default 15
UpdatedAt                   DateTimeOffset
```

Single row, same pattern as `LocationSetting`. When absent, defaults are used.

---

## API Changes

### New Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/settings/display` | Returns `DisplaySettingDto` or defaults if not set |
| PUT | `/api/settings/display` | Body: `DisplaySettingDto` → validates → persists → returns updated dto |

### DisplaySettingDto

```csharp
public record DisplaySettingDto(
    double SurfaceMultiplier,
    bool OpaqueSurfaces,
    int TransitionDurationSecs);
```

### Validation (FluentValidation in Core)

- `SurfaceMultiplier`: 0.0 to 2.0 inclusive
- `TransitionDurationSecs`: 0 to 60 inclusive

---

## Blazor UI Changes

### Bootstrap Removal

1. Remove `<link rel="stylesheet" href="lib/bootstrap/dist/css/bootstrap.min.css" />` from `index.html`
2. Remove `lib/bootstrap/` directory from `wwwroot`
3. Replace all Bootstrap classes in `.razor` files with custom CSS equivalents
4. Remove Bootstrap button overrides from `app.css` (no longer needed)

### Bootstrap Class Migration

A mapping of Bootstrap classes to custom CSS replacements:

| Bootstrap Class | Custom Replacement |
|---|---|
| `d-flex` | `.flex` |
| `flex-column` | `.flex-col` |
| `align-items-center` | `.items-center` |
| `justify-content-between` | `.justify-between` |
| `justify-content-center` | `.justify-center` |
| `text-center` | `.text-center` |
| `mb-1` through `mb-5` | `.mb-1` through `.mb-5` (custom scale) |
| `mt-1` through `mt-5` | `.mt-1` through `.mt-5` |
| `ms-1` through `ms-5` | `.ms-1` through `.ms-5` |
| `px-1` through `px-5` | `.px-1` through `.px-5` |
| `py-1` through `py-5` | `.py-1` through `.py-5` |
| `p-1` through `p-5` | `.p-1` through `.p-5` |
| `gap-1` through `gap-4` | `.gap-1` through `.gap-4` |
| `text-muted` | Use `var(--theme-text-muted)` directly |
| `text-danger` | `.text-danger` (kept, redefined) |
| `visually-hidden` | `.sr-only` |
| `d-block` | `.block` |
| `d-none` | `.hidden` |
| `btn btn-primary` | `.btn .btn-primary` (custom) |
| `btn btn-secondary` | `.btn .btn-secondary` (custom) |
| `btn btn-outline-primary` | `.btn .btn-glass` (custom) |
| `btn btn-outline-danger` | `.btn .btn-danger` (custom) |
| `btn btn-success btn-lg` | `.btn .btn-primary .btn-lg` |
| `btn-group` | `.btn-group` (custom) |
| `btn-close` | `.btn-close` (custom) |
| `modal fade show d-block` | `.modal.active` (custom) |
| `modal-dialog modal-dialog-centered` | `.modal-dialog` (custom, always centered) |
| `modal-content/header/body/footer` | `.modal-content/header/body/footer` (custom) |
| `form-control` | `.form-input` (custom) |
| `form-label` | `.form-label` (custom) |
| `form-check` | `.form-check` (custom) |
| `nav nav-tabs` | `.view-tabs` (custom) |
| `nav-item`, `nav-link` | `.view-tab` (custom) |
| `alert alert-warning` | `.alert .alert-warning` (custom) |
| `alert alert-danger` | `.alert .alert-danger` (custom) |
| `spinner-border` | `.spinner` (custom CSS animation) |
| `row`, `col-6` | `.grid-2` (2-column grid) |

### Custom Utility Scale

Spacing utilities use a 4px base:

| Class | Value |
|---|---|
| `-1` | `4px` |
| `-2` | `8px` |
| `-3` | `16px` |
| `-4` | `24px` |
| `-5` | `40px` |

### CSS File Structure

All styles remain in a single `app.css` file (no splitting). Sections:

1. `@font-face` declarations
2. `@property` registrations (existing + new glass variables)
3. CSS reset / base styles
4. Theme blocks (`[data-theme="..."]`)
5. Layout utilities (flex, grid, spacing, text)
6. DOM layers (`#theme-bg`, `#weather-overlay`, `#app`)
7. Component styles (header, tabs, buttons, cards, calendar, modal, forms, settings)
8. Spinner animation

### DisplaySettingService (new Blazor service)

- `InitialiseAsync()`: calls `GET /api/settings/display`, applies values to CSS custom properties via JS interop
- `UpdateAsync(DisplaySettingDto)`: calls `PUT /api/settings/display`, applies new values immediately
- JS interop function `display.setProperty(name, value)` sets CSS custom properties on `document.body`

### theme.js Updates

Add:
```js
export function setDisplayProperty(name, value) {
  document.body.style.setProperty(name, value);
}
```

Used for real-time slider updates without a full theme switch.

---

## Theme Transition Duration Change

The default transition duration changes from `45s` to `15s`:

- All `transition` declarations in `app.css` that previously used `45s` now use `var(--theme-transition-duration, 15s)`
- The `@property` registration for `--theme-transition-duration` uses `initial-value: 15s`
- The Settings slider allows tuning from 0s (instant) to 60s
- The `#theme-bg` gradient transitions use the same variable

---

## MainLayout.razor.css and NavMenu.razor.css

### Cleanup

Both scoped CSS files contain styling for a sidebar layout that is not used in the current design:

- `MainLayout.razor.css`: sidebar gradient, top-row styling, responsive sidebar — all dead CSS
- `NavMenu.razor.css`: navbar toggler, nav-item styling, dark theme nav — all dead CSS

Both files should be either emptied or removed entirely. All layout styling lives in `app.css`.

If `NavMenu.razor` is still needed (for the current navbar structure), its styling should move to `app.css` and use theme variables. If the navbar is replaced by the `DashboardHeader` component, `NavMenu.razor` and its CSS file should be removed.

---

## Performance Constraints

All the rules from the theming spec apply, plus:

- **No animated box-shadows.** Shadows are static per element; only colours transition via custom properties.
- **Max 2 transparency layers.** gradient → glass card → event pill. Never nest a glass card inside another glass card.
- **`box-shadow` complexity:** maximum 3 shadow layers per element (ring + elevation + inset highlight). This is well within Pi GPU capability for static shadows.
- **Slider updates:** use `requestAnimationFrame` for real-time CSS custom property updates during drag, not per-`input` event. This prevents layout thrashing.
- **Font loading:** WOFF2 format only (smallest file size). Subset to Latin + Latin Extended if the full charset is large.

---

## Touch & Accessibility

All requirements from the theming spec carry forward:

- Minimum touch target: **48 × 48 px** on all interactive elements
- WCAG AA text contrast in all four themes
- Sliders: native `<input type="range">` for built-in touch/accessibility support
- Modal: focus trap, Escape key (if physical keyboard attached), touch-friendly close button
- Inputs: `scrollIntoView` + `env(keyboard-inset-height)` pattern for virtual keyboard

---

## Weather Overlay Compatibility

The `#weather-overlay` div at z-index 1 remains unchanged. The glassmorphism-lite surface design is deliberately compatible with future weather animations:

- Semi-transparent card surfaces allow weather particles (rain, snow) to be subtly visible behind the UI
- The `--theme-surface-opacity` setting doubles as a weather-visibility tuning knob — lower opacity means more weather visible through cards
- The "Opaque surfaces" toggle provides a full escape hatch if weather + transparency causes performance issues on the Pi
- Weather animations will be CSS `@keyframes` on `transform` and `opacity` only, operating on the `#weather-overlay` layer independently of the app layer

---

## Documentation Updates

As each implementation step lands, the following docs and skills must be kept in sync:

- `.agent/docs/ui-design-system.md` — update with new glass CSS variables, updated transition duration default, surface treatment spec, new component patterns
- `.agent/docs/architecture.md` — update with DisplaySetting entity, new API endpoints, DisplaySettingService
- `.agent/skills/ui-theming/SKILL.md` — update with glass surface rules, Bootstrap removal notes, new utility class names
- `.agent/skills/frontend-design/SKILL.md` — update if component patterns or constraints change

These updates happen incrementally as each step is completed, not batched at the end.

---

## Out of Scope

- Weather animation implementation (future spec)
- Changing calendar data APIs or event behaviour
- Modifying the circadian colour palettes
- Per-user display preferences (single-user kiosk)
- Dark mode toggle (the circadian system handles this naturally)
