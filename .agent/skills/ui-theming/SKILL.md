---
name: ui-theming
description: FamilyHQ project-specific UI theming rules. Load whenever modifying CSS, adding components, editing layout, or building new pages. Governs the time-of-day theme system, CSS variable usage, layer model, touch targets, and Pi 3B+ performance constraints.
---

# FamilyHQ UI Theming Skill

Load this skill for any task that touches CSS, Blazor components, layout, or the `wwwroot` directory.

**Full reference**: `.agent/docs/ui-design-system.md`

## Hard Rules (never violate)

1. **No hardcoded colours** — every colour must reference a CSS custom property from the theme system (e.g. `var(--theme-accent)`, `var(--theme-surface)`).
2. **No `backdrop-filter: blur()`** — crashes the Pi 3B+ GPU.
3. **No heavy JS loops** — no `setInterval`/`requestAnimationFrame` for animation. CSS transitions only.
4. **Min touch target: 48 × 48 px** — all buttons, links, tabs, chips, and interactive elements.
5. **Portrait-first layout** — the display is 1080 × 1920. Design for vertical space, not horizontal. Scrollable containers are preferred over horizontal layouts on small screens.
6. **Never add content inside `#theme-bg` or `#weather-overlay`** — these are reserved visual layers.
7. **No Bootstrap** — Bootstrap has been fully removed. Do not re-add Bootstrap classes or the Bootstrap stylesheet. All styles live in `wwwroot/css/app.css`.
8. **Use `.glass-surface` for all panels/cards** — it wires up background, border, and box-shadow to the glass variable system automatically. **Modals use `--theme-modal-bg` instead** (solid, theme-matched background for readability).

## Theme Variables Quick Reference

Use these on every component:

| Purpose | Variable |
|---|---|
| Page/card background | `.glass-surface` class (preferred) or `var(--theme-surface)` |
| Card/panel border | `var(--theme-glass-border)` (via `.glass-surface`) |
| Body text | `var(--theme-text)` |
| Labels, secondary text | `var(--theme-text-muted)` |
| Buttons, active states, event pills | `var(--theme-accent)` |
| Hover state | `var(--theme-accent-hover)` |
| Today cell | `var(--theme-today)` |
| Weekend cell | `var(--theme-weekend)` |
| Surface opacity (glass level) | `var(--theme-surface-opacity)` × `var(--user-surface-multiplier)` |
| Theme transition speed | `var(--theme-transition-duration)` (default `15s`) |
| Dividers inside panels | `var(--theme-glass-divider)` |
| Modal background | `var(--theme-modal-bg)` (solid, not glass) |

The background gradient is handled by `#theme-bg` automatically — **do not set background on `body` or `#app`**.

### Glass Surface Variables

The glass system adds these per-theme variables (set inside each `[data-theme="..."]` block):

| Variable | Purpose |
|---|---|
| `--theme-surface-opacity` | Base opacity for glass surfaces (morning/daytime: 0.55, evening: 0.12, night: 0.08) |
| `--theme-glass-border` | 1px white-tinted border colour |
| `--theme-glass-ring` | Outer glow ring (box-shadow layer 1) |
| `--theme-glass-shadow` | Drop shadow (box-shadow layer 2) |
| `--theme-glass-highlight` | Top-edge highlight (box-shadow layer 3, inset) |
| `--theme-glass-divider` | Divider lines inside panels |

### Shape Language

| Element | Border Radius |
|---|---|
| Cards, panels, modals | `8px` |
| Buttons | `6px` |
| Table cells | `4px` |

### Available Component Classes

Defined in `wwwroot/css/app.css` — use these instead of Bootstrap equivalents:

- **Buttons**: `.btn-primary`, `.btn-secondary`, `.btn-ghost`, `.btn-danger`
- **Tabs**: `.view-tabs` (container), `.view-tab`, `.view-tab.active`
- **Modals**: `.modal-overlay`, `.modal-container`, `.modal-header`, `.modal-body`, `.modal-footer`
- **Forms**: `.form-group`, `.form-label`, `.form-input`, `.form-select`
- **Alerts**: `.alert-info`, `.alert-success`, `.alert-warning`, `.alert-error`
- **Spinner**: `.spinner`
- **Flex utilities**: `.flex`, `.flex-col`, `.items-center`, `.items-start`, `.items-end`, `.justify-between`, `.justify-center`, `.justify-end`
- **Spacing**: `.mb-1`–`.mb-5`, `.mt-1`–`.mt-5`, `.gap-1`–`.gap-4`
- **Misc**: `.w-full`, `.text-center`, `.text-right`

## Adding a New Themed Component — Checklist

- [ ] Uses only CSS variables from the table above — no hardcoded colours
- [ ] Surfaces use `.glass-surface` class (handles background, border, and shadows)
- [ ] Border radius: `8px` cards/panels, `6px` buttons, `4px` cells
- [ ] Has `transition: background-color var(--theme-transition-duration) ease-in-out, color var(--theme-transition-duration) ease-in-out` if always visible
- [ ] Touch targets ≥ 48 × 48 px
- [ ] Tested visually in all 4 themes (morning / daytime / evening / night)
- [ ] No `backdrop-filter` or heavy filter effects
- [ ] No Bootstrap classes

## Input Fields & Virtual Keyboard

Any `<input>` or form field must:
1. Live inside a scrollable container (`overflow-y: auto`).
2. Call `element.scrollIntoView({ behavior: 'smooth', block: 'center' })` on focus (JS interop from Blazor `@onfocus`).
3. Container must have `padding-bottom: env(keyboard-inset-height, 0px)`.

## Theme Switching Flow

- `data-theme` attribute on `<body>` controls which theme is active.
- Set via `theme.js` → `document.body.setAttribute('data-theme', period)`.
- Called by `ThemeService` on load (from `/api/daytheme/today`) and on `SignalRService.OnThemeChanged`.
- CSS `@property` transitions handle the 45-second colour bleed automatically.

## Weather Overlay

The `#weather-overlay` div renders CSS-only weather animations via `WeatherOverlay.razor` + `weather.js`. Classes like `.weather-lightrain`, `.weather-snow`, `.weather-thunder`, etc. are applied based on current conditions. The `.weather-windy` modifier tilts particle animations. All animations use `transform`/`opacity` only with `will-change` for GPU compositing. Rules:
- CSS-only animations (no canvas/WebGL).
- `pointer-events: none` always.
- Must not interfere with `#theme-bg` or `#app`.
- Never place content inside `#weather-overlay` — pseudo-elements handle all visuals.

## When This Skill Does NOT Apply

- Pure backend C# changes with no UI impact.
- E2E test files.
- Build/CI configuration.
