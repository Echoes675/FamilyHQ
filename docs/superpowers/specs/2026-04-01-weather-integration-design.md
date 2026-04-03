# Weather Integration Design

## Overview

Integrate real-time weather data into FamilyHQ, displaying current conditions, forecasts, and animated weather overlays. The system polls Open-Meteo (or a simulator mimicking its API) for weather data, stores it in PostgreSQL, and pushes updates to the Blazor UI via SignalR.

## Goals

- Display current weather, 5-day forecast, 14-day forecast, and hourly forecast across all dashboard views
- Animate a weather overlay layer on the background (CSS-only, Pi 3B+ safe)
- Abstract the weather provider behind a URL-based configuration so the same code runs against Open-Meteo in production and the simulator in dev/staging
- Enable E2E test isolation via per-user location settings and per-location weather data in the simulator

## Provider

**Open-Meteo** — free, no API key required, returns current + hourly + daily forecast (up to 16 days) in a single request. Attribution required for non-commercial use.

### Provider Abstraction

A single `OpenMeteoWeatherProvider` class takes a base URL from configuration. In production the URL points to `https://api.open-meteo.com`, in dev/staging it points to the FamilyHQ Simulator which exposes the same endpoint contract. No conditional DI registration — same code path everywhere.

The domain layer works with its own weather models (`WeatherCondition`, `WeatherDataPoint`, etc.). The provider maps Open-Meteo's WMO codes and response shape into these domain models. Swapping to a different provider in the future means writing a new provider class and adjusting request/response mapping — no changes to business logic or UI.

```
IWeatherProvider
  └── OpenMeteoWeatherProvider (base URL from config)
        ├── Production: https://api.open-meteo.com
        └── Dev/Staging: https://localhost:7199 (Simulator)
```

## Weather States

### Primary Conditions (WeatherCondition enum)

| Condition | WMO Codes | Visual Character |
|---|---|---|
| Clear | 0 | Open sky |
| PartlyCloudy | 1, 2 | Scattered clouds |
| Cloudy | 3 | Full overcast |
| Fog | 45, 48 | Dense low visibility / soft haze |
| Drizzle | 51, 53, 55 | Very light drops |
| LightRain | 61, 80 | Gentle rain |
| HeavyRain | 63, 65, 81, 82 | Downpour |
| Thunder | 95, 96, 99 | Lightning + rain |
| Snow | 71, 73, 75, 77, 85, 86 | Snowfall |
| Sleet | 56, 57, 66, 67 | Mixed precipitation |

### Wind Modifier

Wind is not a standalone state — it is a composable modifier that overlays on any primary condition. Triggered when wind speed exceeds a configurable threshold (default: 30 km/h). The overlay animation tilts particle angles ~30° and increases horizontal drift. The `IsWindy` flag is derived from the wind speed measurement.

## Backend Architecture

### Domain Models (FamilyHQ.Core)

**WeatherCondition enum** — as listed above.

**WeatherDataPoint entity (DB-stored):**
- `Id` (int, PK)
- `LocationSettingId` (FK to LocationSetting)
- `Timestamp` (DateTimeOffset) — the time this data point applies to
- `Condition` (WeatherCondition)
- `TemperatureCelsius` (double)
- `HighCelsius` (double?, daily records only)
- `LowCelsius` (double?, daily records only)
- `WindSpeedKmh` (double)
- `IsWindy` (bool, derived from threshold)
- `DataType` (enum: Current, Hourly, Daily)
- `RetrievedAt` (DateTimeOffset) — when the data was fetched

**WeatherSetting entity (DB-stored):**
- `Id` (int, PK)
- `Enabled` (bool, default true)
- `PollIntervalMinutes` (int, default 30, minimum 1)
- `TemperatureUnit` (enum: Celsius, Fahrenheit — default Celsius)
- `WindThresholdKmh` (double, default 30)
- `ApiKey` (string?, encrypted — for future providers that require one)

### DTOs (FamilyHQ.Core)

- `CurrentWeatherDto` — condition, temperature (in user's preferred unit), isWindy, windSpeedKmh, iconName
- `HourlyForecastItemDto` — hour (DateTimeOffset), condition, temperature, isWindy, iconName
- `DailyForecastItemDto` — date (DateOnly), condition, high, low, isWindy, iconName
- `WeatherSettingDto` — enabled, pollIntervalMinutes, temperatureUnit, windThresholdKmh, apiKey (masked for reads)

### Provider Interface (FamilyHQ.Core)

```csharp
public interface IWeatherProvider
{
    Task<WeatherResponse> GetWeatherAsync(double latitude, double longitude, CancellationToken ct);
}
```

The interface and `WeatherResponse` live in Core so both Services and WebApi can reference them without circular dependencies.

`WeatherResponse` contains:
- `CurrentCondition`, `CurrentTemperatureCelsius`, `CurrentWindSpeedKmh`
- `HourlyForecasts` — list of hourly data points (up to 16 days)
- `DailyForecasts` — list of daily data points (up to 16 days)

`OpenMeteoWeatherProvider`:
- Injected with `HttpClient` configured with `WeatherOptions.BaseUrl`
- Calls the forecast endpoint with required parameters (latitude, longitude, current, hourly, daily fields)
- Maps WMO weather codes to `WeatherCondition` enum
- Maps response JSON to `WeatherResponse` domain model

### Weather Service (FamilyHQ.Services)

```csharp
public interface IWeatherService
{
    Task<CurrentWeatherDto> GetCurrentAsync(CancellationToken ct);
    Task<List<HourlyForecastItemDto>> GetHourlyAsync(DateOnly date, CancellationToken ct);
    Task<List<DailyForecastItemDto>> GetDailyForecastAsync(int days, CancellationToken ct);
}
```

- Reads from the database (data stored by the poller)
- Temperature conversion (C to F) applied here based on user's `WeatherSetting.TemperatureUnit`
- Uses the user's configured location (reuses existing `ILocationService`)

### Weather Poller (FamilyHQ.WebApi — IHostedService)

`WeatherPollerService` — modelled after `DayThemeSchedulerService`:

1. On startup: check if weather is enabled in settings
2. Get the user's location via `ILocationService`
3. Call `IWeatherProvider.GetWeatherAsync(lat, lon)`
4. Store/replace weather data points in the database (keyed by location + data type)
5. Broadcast `WeatherUpdated` via SignalR (`IHubContext<CalendarHub>`)
6. Sleep for the configured poll interval
7. Repeat

If weather is disabled, the poller sleeps and checks periodically (e.g. every 5 minutes) whether it has been re-enabled.

### API Endpoints

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/weather/current` | Current conditions |
| `GET` | `/api/weather/hourly?date=yyyy-MM-dd` | Hourly forecast for a specific day |
| `GET` | `/api/weather/forecast?days=5` | Daily forecast (accepts 5 or 14) |
| `GET` | `/api/settings/weather` | Weather settings |
| `PUT` | `/api/settings/weather` | Update weather settings |

### SignalR

New message type on `CalendarHub`:
- **`WeatherUpdated`** — no parameters. Signals the UI to fetch fresh weather data via the HTTP endpoints. Consistent with the existing `EventsUpdated` pattern.

### Configuration (appsettings.json)

```json
{
  "Weather": {
    "BaseUrl": "https://api.open-meteo.com",
    "PollIntervalMinutes": 30,
    "MinPollIntervalMinutes": 1,
    "WindThresholdKmh": 30,
    "Enabled": true
  }
}
```

- `BaseUrl` — points to Open-Meteo in production, simulator in dev/staging
- Default settings are overridable via the Settings UI (stored in DB `WeatherSetting`)
- DB settings take precedence over appsettings defaults at runtime

## Simulator Extension

### Open-Meteo Compatible Endpoint

`GET /v1/forecast?latitude=X&longitude=Y&current=...&hourly=...&daily=...`

- Returns JSON matching Open-Meteo's response contract
- Reads weather data from the simulator's database, keyed by location coordinates
- If no data exists for a location, returns clear/default conditions

### Backdoor Endpoint for Tests

`POST /backdoor/weather`

Request body — granular per-day/per-hour weather setup:

```json
{
  "latitude": 53.35,
  "longitude": -6.26,
  "current": {
    "weatherCode": 3,
    "temperature": 14.0,
    "windSpeed": 25.0
  },
  "hourly": [
    { "time": "2026-04-01T09:00", "weatherCode": 0, "temperature": 12.0, "windSpeed": 10.0 },
    { "time": "2026-04-01T10:00", "weatherCode": 61, "temperature": 11.0, "windSpeed": 35.0 }
  ],
  "daily": [
    { "date": "2026-04-01", "weatherCode": 61, "temperatureMax": 14.0, "temperatureMin": 8.0, "windSpeedMax": 35.0 },
    { "date": "2026-04-02", "weatherCode": 0, "temperatureMax": 18.0, "temperatureMin": 9.0, "windSpeedMax": 12.0 }
  ]
}
```

### Test Isolation

Each E2E test scenario uses an isolated user with a unique location. The test sets weather data for that location via the backdoor. Parallel tests with different users/locations do not collide — the simulator stores and returns weather data scoped by location coordinates.

## Frontend Architecture

### WeatherService (Blazor WASM)

`WeatherService` / `IWeatherService` — follows the same pattern as `ThemeService`:

- On initialisation: fetches current weather + forecast data via HTTP
- Subscribes to `SignalRService.OnWeatherUpdated`
- On signal: re-fetches data from the API and updates internal state
- Exposes events (`OnWeatherChanged`) for components to subscribe to
- Components update in-place when data changes — no page reload or re-render flicker

### Weather Overlay (Background Animation)

**Component:** `WeatherOverlay.razor`

Renders CSS animations into the existing `#weather-overlay` div (z-index 1, `pointer-events: none`). Uses JS interop to set the overlay content.

**Pi 3B+ Performance Constraints:**
- CSS-only animations — `@keyframes` on `transform` and `opacity` only
- No `backdrop-filter`, no Canvas/WebGL, no heavy JS loops
- Max ~30 animated pseudo-elements
- Use `will-change: transform` sparingly
- Animations use `linear-gradient` shapes for rain drops and snow flakes

**Animation Mapping:**

| Condition | Animation |
|---|---|
| Clear | None (empty overlay) |
| PartlyCloudy | Subtle drifting cloud shapes (opacity + translateX, slow) |
| Cloudy | Darker, slower drifting clouds, more coverage |
| Fog | Full-width semi-transparent gradient overlay, slow opacity pulse |
| Drizzle | Sparse thin falling lines, slow descent |
| LightRain | Moderate falling lines, medium speed |
| HeavyRain | Dense falling lines, fast descent |
| Thunder | HeavyRain animation + periodic full-overlay opacity flash (lightning) |
| Snow | Falling dots/circles, slow, gentle side-to-side drift |
| Sleet | Mix of rain lines and snow dots |

**Wind Modifier:**
When `IsWindy` is true, the particle container gets an additional CSS class that:
- Tilts the animation angle ~30° (via `transform: rotate()`)
- Increases horizontal translation in the keyframe
- Applies to rain, snow, drizzle, and sleet animations

**State Transitions:**
When the weather condition changes, the overlay fades out the current animation (opacity transition ~1s), swaps the content, and fades in the new animation.

### Weather Icons

SVG-based icons — not emoji. A small set of inline SVGs that use `currentColor` for theme-aware colouring. Each `WeatherCondition` maps to one icon. The wind modifier adds a small wind-lines indicator alongside the primary icon.

Icon set: 10 primary condition icons + 1 wind modifier icon.

### Weather Strip (All Pages)

**Component:** `WeatherStrip.razor`

A thin glass-surface strip below the dashboard header. Present on all views. Content adapts based on the active view:

| View | Left Side (Current) | Right Side (Forecast) |
|---|---|---|
| **Month** | Icon + temp + condition text | 5-day forecast: day name + icon + high°/low° |
| **Day** | Icon + temp + condition text | Today summary: high°/low° + condition |
| **Agenda** | Icon + temp + condition text | 3-day mini forecast: day name + icon + high°/low° |

The strip uses `.glass-surface` styling and theme variables. It includes wind speed display when wind is notable.

### Month View — 5-Day Forecast

Integrated into the weather strip (Option C from brainstorm). The strip shows:
- Left: current weather icon + temperature + condition name
- Right: 5 days, each with day name, weather icon, and high°/low° temperatures
- Separated by a subtle vertical divider

### Agenda View — Weather in Date Cells

Each date cell (leftmost column) in the agenda table shows (Option A from brainstorm):
- Date and month (existing)
- Below: weather icon + high°/low° temperatures
- Displayed for days that fall within the 14-day forecast range
- Days beyond forecast range show no weather data

### Day View — Hourly Weather

Inline with the time label in the time column (Option A from brainstorm):
- Each hour row: `09:00  ☀️  12°`
- Time + icon + temperature on one line
- Only shown for hours that have forecast data available

### Day View Weather Strip

The day view strip shows current weather on the left and today's high/low + condition on the right. The hourly detail is shown inline in the time column, so the strip stays compact.

## Settings — Weather Sub-Page

Accessed from the Settings page as a new section between Display and Today's Theme Schedule. Tapping navigates to a dedicated weather settings page.

**Settings Fields:**
- **Enable/Disable** — toggle switch
- **Temperature Unit** — Celsius / Fahrenheit selector (default: Celsius)
- **Poll Interval** — numeric input in minutes (min: 1, default: 30)
- **Provider URL** — read-only display of the configured base URL (from appsettings)
- **API Key** — masked text input, only shown when the provider requires one (future use)

Back arrow returns to the main settings page. Changes are saved via `PUT /api/settings/weather`.

When weather is toggled off:
- Weather strip hides
- Weather overlay clears
- Agenda/day view weather data hides
- Poller stops fetching

## Database Changes

New tables:

### WeatherDataPoints
| Column | Type | Notes |
|---|---|---|
| Id | int (PK) | Auto-increment |
| LocationSettingId | int (FK) | References LocationSettings |
| Timestamp | timestamptz | Time this data applies to |
| Condition | int | WeatherCondition enum |
| TemperatureCelsius | double | Always stored in Celsius |
| HighCelsius | double? | Daily records only |
| LowCelsius | double? | Daily records only |
| WindSpeedKmh | double | |
| IsWindy | bool | Derived from threshold |
| DataType | int | Current=0, Hourly=1, Daily=2 |
| RetrievedAt | timestamptz | When fetched from provider |

### WeatherSettings
| Column | Type | Notes |
|---|---|---|
| Id | int (PK) | Single row |
| Enabled | bool | Default true |
| PollIntervalMinutes | int | Default 30, min 1 |
| TemperatureUnit | int | Celsius=0, Fahrenheit=1 |
| WindThresholdKmh | double | Default 30 |
| ApiKey | string? | Encrypted, for future providers |

## Validation (FluentValidation in FamilyHQ.Core)

**WeatherSettingValidator:**
- `PollIntervalMinutes` >= 1
- `WindThresholdKmh` > 0
- `TemperatureUnit` must be a valid enum value

## Error Handling

- Provider HTTP failures: log warning, retain last known data, retry on next poll cycle
- No location configured: poller logs info and waits — no weather data served until location is set
- Provider returns unexpected data: log error, skip update, retain last known data
- Weather disabled: endpoints return 204 No Content, overlay and widgets hide gracefully
