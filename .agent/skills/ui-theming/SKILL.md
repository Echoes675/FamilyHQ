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

## Theme Variables Quick Reference

Use these on every component:

| Purpose | Variable |
|---|---|
| Page/card background | `var(--theme-surface)` |
| Card/panel border | `var(--theme-border)` |
| Body text | `var(--theme-text)` |
| Labels, secondary text | `var(--theme-text-muted)` |
| Buttons, active states, event pills | `var(--theme-accent)` |
| Hover state | `var(--theme-accent-hover)` |
| Today cell | `var(--theme-today)` |
| Weekend cell | `var(--theme-weekend)` |

The background gradient is handled by `#theme-bg` automatically — **do not set background on `body` or `#app`**.

## Adding a New Themed Component — Checklist

- [ ] Uses only CSS variables from the table above
- [ ] Has `transition: background-color 45s ease-in-out, color 45s ease-in-out` if always visible
- [ ] Touch targets ≥ 48 × 48 px
- [ ] Tested visually in all 4 themes (morning / daytime / evening / night)
- [ ] No `backdrop-filter` or heavy filter effects

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

## Weather Overlay Extension Point

Future weather animations go in `<div id="weather-overlay">`. Rules:
- CSS-only or minimal JS animations.
- `pointer-events: none` always.
- Must not interfere with `#theme-bg` or `#app`.

## When This Skill Does NOT Apply

- Pure backend C# changes with no UI impact.
- E2E test files.
- Build/CI configuration.
