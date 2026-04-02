# Architecture & Structure

## Project Layout
- src/FamilyHQ.WebUi/: Blazor WASM UI.
- src/FamilyHQ.WebApi/: ASP.NET Core API.
- src/FamilyHQ.Services/: Business logic and orchestration.
- src/FamilyHQ.Data/: EF Core context
- src/FamilyHQ.Data.PostgreSQL/: PostgreSQL specific implementation and migrations.
- src/FamilyHQ.Core/: Shared Models, DTOs, and FluentValidation logic.

## Deployment Context
- **Kiosk device**: Raspberry Pi 3B+ running Chromium in kiosk mode (`--kiosk --touch-events=enabled`).
- **Display**: 27" 1080p touchscreen in **portrait orientation** (1080×1920 effective).
- **No physical keyboard/mouse**: all input is touch. Virtual keyboard (`matchbox-keyboard` or `onboard`) is invoked automatically by Chromium on input focus.
- **WebApi** is deployed to a separate web server; the Pi accesses it over the network.
- **Performance constraint**: avoid `backdrop-filter: blur()`, heavy JS animation loops, and canvas/WebGL — all are too expensive for the Pi 3B+ GPU/CPU.

## Dependency Rules
- Directional Flow: Dependencies must flow inward.
-- WebUi and WebApi -> Services -> Data -> Core.
-- Forbidden: Never add references from Core or Services back to the Web projects.
- Shared Logic: All DTOs, Enums, and Constants used by both Client and Server must reside in FamilyHQ.Core.

## Technical Principles
- Clean Architecture: Ensure the WebApi and WebUi projects only depend on Services or Core.
- Infrastructure Isolation: External integrations (e.g., Google Calendar) must be abstracted behind interfaces.
- Shared Validation: Use FluentValidation in FamilyHQ.Core so it can be executed on both the Blazor client and the ASP.NET server.

## Key Entities
- **CalendarEvent**: Google Calendar event data.
- **DayTheme**: Stores the 4 time-of-day period boundaries (MorningStart, DaytimeStart, EveningStart, NightStart as TimeOnly) for a given Date. Calculated once per day by DayThemeSchedulerService using sunrise/sunset for the configured location.
- **LocationSetting**: Stores the user's configured location (PlaceName, Latitude, Longitude). A single row; when absent, the API falls back to IP-based geolocation.
- **DisplaySetting**: Stores user display preferences (SurfaceMultiplier as `double`, OpaqueSurfaces as `bool`, TransitionDurationSecs as `int`). A single row. Controls the glassmorphism surface opacity and theme transition speed at runtime.
- **WeatherDataPoint**: Stores weather data (current, hourly, daily) for a location. Keyed by LocationSettingId + DataType + Timestamp.
- **WeatherSetting**: Stores weather preferences (enabled, poll interval, temperature unit, wind threshold). Single row.

## Key Services
- **ISunCalculatorService / SunCalculatorService**: Calculates sunrise/sunset times for a lat/lon using the SunCalcNet NuGet package.
- **IDayThemeService / DayThemeService**: Calculates and persists today's DayTheme boundaries.
- **DayThemeSchedulerService** (IHostedService): On startup, ensures today's DayTheme exists. Loops using Task.Delay to wake at each period boundary and broadcast `ThemeChanged(periodName)` to all SignalR clients via IHubContext<CalendarHub>.
- **ILocationService / LocationService**: Returns the effective location — saved LocationSetting from DB if present, otherwise IP-based geolocation (ip-api.com free tier) as fallback.
- **IGeocodingService / GeocodingService**: Geocodes a place name string to lat/lon using the Nominatim (OpenStreetMap) API. No API key required.
- **IDisplaySettingService / DisplaySettingService** (Blazor WASM): Loads display preferences from `GET /api/settings/display` on startup and applies `--user-surface-multiplier` and `--theme-transition-duration` CSS custom properties via JS interop. Saves changes via `PUT /api/settings/display`.
- **IWeatherProvider / OpenMeteoWeatherProvider**: Fetches weather data from Open-Meteo (or simulator). Base URL from config — same code in all environments.
- **IWeatherService / WeatherService**: Reads stored weather data, applies temperature conversion, serves DTOs.
- **WeatherPollerService** (IHostedService): Background poller that fetches weather data at configurable intervals and broadcasts `WeatherUpdated` via SignalR.
- **IWeatherUiService / WeatherUiService** (Blazor WASM): Fetches weather data via HTTP, subscribes to SignalR `WeatherUpdated` events, exposes `OnWeatherChanged` for components.

## API Endpoints
- `GET  /api/daytheme/today` → DayThemeDto (Date + 4 boundary times + current period)
- `GET  /api/settings/location` → LocationSettingDto or 404
- `POST /api/settings/location` `{ placeName }` → geocodes, saves, returns LocationSettingDto
- `GET  /api/settings/display` → DisplaySettingDto (SurfaceMultiplier, OpaqueSurfaces, TransitionDurationSecs) — returns defaults if no row exists
- `PUT  /api/settings/display` `{ surfaceMultiplier, opaqueSurfaces, transitionDurationSecs }` → upserts the single DisplaySetting row, returns DisplaySettingDto
- `GET  /api/weather/current` → CurrentWeatherDto (condition, temperature, wind)
- `GET  /api/weather/hourly?date=yyyy-MM-dd` → List<HourlyForecastItemDto>
- `GET  /api/weather/forecast?days=5` → List<DailyForecastItemDto>
- `GET  /api/settings/weather` → WeatherSettingDto
- `PUT  /api/settings/weather` → upserts weather settings

## SignalR (CalendarHub — /hubs/calendar)
- **EventsUpdated**: existing — triggers calendar refresh on all clients.
- **ThemeChanged(string period)**: pushed by DayThemeSchedulerService when the current time-of-day period changes. `period` is one of: `"Morning"`, `"Daytime"`, `"Evening"`, `"Night"`.
- **WeatherUpdated**: pushed by WeatherPollerService when new weather data is stored. No parameters — UI fetches fresh data via HTTP.

## UI Layer Architecture
The DOM is structured in three stacked layers to support time-of-day theming and future weather overlays:

```
<body data-theme="morning|daytime|evening|night">
  <div id="theme-bg" />        ← layer 0: full-bleed gradient background (CSS @property transition, 45s)
  <div id="weather-overlay" /> ← layer 1: future weather animations (empty/hidden for now)
  <div id="app">...</div>      ← layer 2: all Blazor UI content (unchanged behaviour)
</body>
```

Theme switching is driven by the `data-theme` attribute on `<body>`. CSS custom properties registered via `@property` (typed as `<color>`) allow the browser to smoothly interpolate gradient colours over a user-configurable duration (default 15s, controlled by `--theme-transition-duration`). See `.agent/docs/ui-design-system.md` for full CSS variable reference.

The UI uses a **glassmorphism-lite** design — semi-transparent `.glass-surface` components with white border glow and layered box-shadows. Bootstrap has been removed; all styles live in `wwwroot/css/app.css`. The DM Sans font is self-hosted.

## Pages & Navigation
- `/` — Dashboard (Month / Day / Agenda views)
- `/settings` — Settings page (Location, Weather, Today's Theme Schedule, Display, Account / Sign Out)
- `/settings/weather` — Weather settings sub-page (enable/disable, temperature unit, poll interval, wind threshold)
- Settings accessed via a gear icon (⚙️) in the DashboardHeader. User name and sign-out are on the Settings page, not the header.

## Performance Targets
- Responsiveness: API endpoints should target < 200ms response time.
- EF Core Efficiency:
-- Use AsNoTracking() for read-only queries.
-- Avoid N+1 issues by using .Include() for required navigation properties.
-- Always implement pagination for list-based endpoints using Skip and Take.
-- Async Execution: Always pass CancellationToken from the Controller through to EF Core async methods (e.g., ToListAsync(ct)).
-- Transactions: Use explicit transactions (IDbContextTransaction) for operations involving multiple SaveChangesAsync calls to ensure atomicity.
- Blazor Optimization: Use @key in loops to help the diffing engine and avoid unnecessary re-renders of heavy components.
