# Kiosk UI Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild FamilyHQ's Blazor WASM frontend as a full-bleed portrait kiosk UI (1080×1920px) with an ambient background system, three calendar views (Today, Week, Month), RRULE recurrence support, weather integration, and user preferences persistence.

**Architecture:** Clean architecture layers preserved. Tailwind CSS via Play CDN (no build step). RRULE expansion server-side only using `Ical.Net` in `FamilyHQ.Services`. Weather via `IWeatherProvider` + `NullWeatherProvider` stub + `WeatherHub` SignalR. User preferences via LocalStorage-first + debounced `PUT /api/preferences`.

**Design Spec:** `docs/superpowers/specs/2026-03-27-kiosk-ui-redesign-design.md`

**Tech Stack:** .NET 10, Blazor WASM, ASP.NET Core, EF Core / PostgreSQL, xUnit + Moq + FluentAssertions, SignalR.

**Pipeline Rule:** After each phase commit, trigger the `FamilyHQ-Dev` Jenkins pipeline using the `jk` CLI skill and wait for it to pass before proceeding to the next phase. Use `jk` to monitor build and E2E test results. Existing E2E tests lock in current behaviour — any existing scenarios broken by a phase's changes must be updated in the same phase commit before the pipeline is triggered.

---

## File Map

### New files

| File | Project | Responsibility |
|------|---------|----------------|
| `src/FamilyHQ.WebUi/Components/DashboardHeader.razor` | WebUi | Year/month nav, view switcher, weather indicator, settings gear |
| `src/FamilyHQ.WebUi/Components/DashboardHeader.razor.css` | WebUi | Scoped styles for header |
| `src/FamilyHQ.WebUi/Components/TodayView.razor` | WebUi | 24-hour vertical time-blocked grid |
| `src/FamilyHQ.WebUi/Components/TodayView.razor.css` | WebUi | Scoped styles for today view |
| `src/FamilyHQ.WebUi/Components/WeekView.razor` | WebUi | 7-day hybrid grid |
| `src/FamilyHQ.WebUi/Components/WeekView.razor.css` | WebUi | Scoped styles for week view |
| `src/FamilyHQ.WebUi/Components/CalendarGrid.razor` | WebUi | Month view: 31-row vertical list |
| `src/FamilyHQ.WebUi/Components/CalendarGrid.razor.css` | WebUi | Scoped styles for month grid |
| `src/FamilyHQ.WebUi/Components/AmbientBackground.razor` | WebUi | Circadian gradient + weather overlay CSS state machine |
| `src/FamilyHQ.WebUi/Components/EventModal.razor` | WebUi | Create/edit event with RRULE picker |
| `src/FamilyHQ.WebUi/Components/RecurrenceEditPrompt.razor` | WebUi | "This instance vs all" modal for recurring edits |
| `src/FamilyHQ.WebUi/Components/DeleteConfirmModal.razor` | WebUi | Deletion confirmation modal |
| `src/FamilyHQ.WebUi/Components/DayDetailModal.razor` | WebUi | Overflow events for a day cell |
| `src/FamilyHQ.WebUi/Components/MonthQuickJumpModal.razor` | WebUi | 12-month grid for direct month selection |
| `src/FamilyHQ.WebUi/Components/SettingsPanel.razor` | WebUi | Column order, colors, density, preferences |
| `src/FamilyHQ.WebUi/Components/BurnInOrbit.razor` | WebUi | 1–2px pixel orbit for burn-in protection |
| `src/FamilyHQ.WebUi/Services/IUserPreferencesApiService.cs` | WebUi | Frontend interface for preferences API |
| `src/FamilyHQ.WebUi/Services/UserPreferencesService.cs` | WebUi | LocalStorage-first + debounced backend sync |
| `src/FamilyHQ.WebUi/ViewModels/UserPreferencesViewModel.cs` | WebUi | Preferences view model |
| `src/FamilyHQ.WebUi/ViewModels/WeatherStateViewModel.cs` | WebUi | Weather state view model |
| `src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs` | Core | Provider-agnostic weather polling contract |
| `src/FamilyHQ.Core/Interfaces/IRruleExpander.cs` | Core | RRULE expansion contract |
| `src/FamilyHQ.Core/Interfaces/IUserPreferencesService.cs` | Core | Load/save UserPreferences contract |
| `src/FamilyHQ.Core/Models/WeatherCondition.cs` | Core | Clear/Cloudy/LightRain/HeavyRain/Thunder/Snow/WindMist enum |
| `src/FamilyHQ.Core/Models/WeatherState.cs` | Core | Current condition + temperature + last-updated |
| `src/FamilyHQ.Core/Models/UserPreferences.cs` | Core | User preferences domain model |
| `src/FamilyHQ.Core/DTOs/WeatherStateDto.cs` | Core | Weather DTO for API/SignalR |
| `src/FamilyHQ.Core/DTOs/UserPreferencesDto.cs` | Core | Preferences DTO for API |
| `src/FamilyHQ.Core/Interfaces/ISolarCalculator.cs` | Core | NOAA solar algorithm contract |
| `src/FamilyHQ.Core/Models/CircadianBoundaries.cs` | Core | date, sunrise, sunset, dawnStart, duskEnd |
| `src/FamilyHQ.Services/Weather/WeatherBackgroundService.cs` | Services | Polls IWeatherProvider, pushes via SignalR |
| `src/FamilyHQ.Services/Weather/NullWeatherProvider.cs` | Services | Stub implementation returning null |
| `src/FamilyHQ.Services/Weather/SimulatorWeatherProvider.cs` | Services | Dev/test IWeatherProvider calling simulator mock endpoint |
| `src/FamilyHQ.Services/Circadian/SolarCalculator.cs` | Services | NOAA solar algorithm implementation |
| `src/FamilyHQ.Services/Circadian/CircadianStateService.cs` | Services | Background service computing daily circadian boundaries |
| `src/FamilyHQ.Services/Options/KioskOptions.cs` | Services | Latitude/Longitude config class |
| `src/FamilyHQ.Services/Calendar/RruleExpander.cs` | Services | Ical.Net-based RRULE expansion |
| `src/FamilyHQ.Services/UserPreferencesService.cs` | Services | Persists preferences to DB |
| `src/FamilyHQ.WebApi/Hubs/WeatherHub.cs` | WebApi | SignalR hub for weather push |
| `src/FamilyHQ.WebApi/Controllers/PreferencesController.cs` | WebApi | GET/PUT /api/preferences |
| `src/FamilyHQ.WebApi/Controllers/CircadianController.cs` | WebApi | GET /api/circadian/current |
| `src/FamilyHQ.Data/Configurations/UserPreferencesConfiguration.cs` | Data | EF config for UserPreferences |
| `src/FamilyHQ.Data/Configurations/CircadianBoundariesConfiguration.cs` | Data | EF config for CircadianBoundaries |
| `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddRruleFields.cs` | Data.PostgreSQL | Adds RecurrenceRule/RecurrenceId/IsRecurrenceException columns |
| `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddUserPreferences.cs` | Data.PostgreSQL | Adds UserPreferences table |
| `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddCircadianBoundaries.cs` | Data.PostgreSQL | Adds CircadianBoundaries table |
| `tools/FamilyHQ.Simulator/Controllers/WeatherController.cs` | Simulator | POST/GET /api/simulator/weather mock endpoints |
| `tests/FamilyHQ.Services.Tests/Calendar/RruleExpanderTests.cs` | Tests | Unit tests for RruleExpander |
| `tests/FamilyHQ.Services.Tests/UserPreferencesServiceTests.cs` | Tests | Unit tests for UserPreferencesService |
| `tests/FamilyHQ.Services.Tests/Weather/WeatherBackgroundServiceTests.cs` | Tests | Unit tests for WeatherBackgroundService |
| `tests/FamilyHQ.Services.Tests/Circadian/SolarCalculatorTests.cs` | Tests | Unit tests for SolarCalculator with known sunrise/sunset dates |
| `tests-e2e/FamilyHQ.E2E.Features/KioskViews.feature` | E2E | BDD scenarios for kiosk views including weather state scenarios |

### Modified files

| File | Change |
|------|--------|
| `src/FamilyHQ.WebUi/wwwroot/index.html` | Remove Bootstrap CSS; add Tailwind Play CDN script + config; add `setAppTransform` JS interop |
| `src/FamilyHQ.WebUi/wwwroot/css/app.css` | Strip to Blazor error UI and loading spinner only; add circadian gradient + weather overlay keyframes |
| `src/FamilyHQ.WebUi/Layout/MainLayout.razor` | Rebuild as full-bleed kiosk shell; host `BurnInOrbit` |
| `src/FamilyHQ.WebUi/Layout/MainLayout.razor.css` | Kiosk layout styles |
| `src/FamilyHQ.WebUi/Pages/Index.razor` | Rebuild as thin shell rendering `AmbientBackground`, `DashboardHeader`, active view |
| `src/FamilyHQ.WebUi/_Imports.razor` | Remove Bootstrap using directives |
| `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs` | Add `GetEventsForRangeAsync` method |
| `src/FamilyHQ.WebUi/Services/CalendarApiService.cs` | Implement `GetEventsForRangeAsync` |
| `src/FamilyHQ.WebUi/Program.cs` | Register `UserPreferencesService`, `IUserPreferencesApiService` |
| `src/FamilyHQ.Core/Models/CalendarEvent.cs` | Add `RecurrenceRule`, `RecurrenceId`, `IsRecurrenceException` |
| `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs` | Add `RecurrenceRule` field |
| `src/FamilyHQ.Core/DTOs/UpdateEventRequest.cs` | Add `RecurrenceRule` field |
| `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs` | Add `GetEventsForRangeAsync`, `UpdateInstanceAsync`, `UpdateSeriesFromAsync`, `UpdateAllInSeriesAsync`, `DeleteInstanceAsync`, `DeleteSeriesFromAsync`, `DeleteAllInSeriesAsync` |
| `src/FamilyHQ.Services/Calendar/CalendarEventService.cs` | Implement all three recurrence edit scope methods; integrate `IRruleExpander` |
| `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs` | Handle exception instances, series splits, and master updates for Google Calendar sync |
| `src/FamilyHQ.Services/ServiceCollectionExtensions.cs` | Register `IRruleExpander`, `IWeatherProvider`, `WeatherBackgroundService`, `IUserPreferencesService`, `CircadianStateService` |
| `src/FamilyHQ.WebApi/Controllers/EventsController.cs` | Add `GET /api/events/range` endpoint |
| `src/FamilyHQ.WebApi/Program.cs` | Register `WeatherHub`, map `/hubs/weather`; register `CircadianController` |
| `src/FamilyHQ.WebApi/appsettings.json` | Add `Kiosk:Latitude` and `Kiosk:Longitude` |
| `src/FamilyHQ.Data/FamilyHqDbContext.cs` | Add `DbSet<UserPreferences>`, `DbSet<CircadianBoundaries>` |
| `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs` | Add RRULE column configurations |
| `src/FamilyHQ.Services/FamilyHQ.Services.csproj` | Add `Ical.Net` NuGet package reference |
| `Directory.Packages.props` | Add `Ical.Net` package version |
| `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs` | Update for all three recurrence edit scope methods |

### Deleted files

| File | Reason |
|------|--------|
| `src/FamilyHQ.WebUi/Layout/NavMenu.razor` | Navigation moves into `DashboardHeader` |
| `src/FamilyHQ.WebUi/Layout/NavMenu.razor.css` | Deleted with NavMenu |

---

## Phase 1 — Foundation: Git Branch + Tailwind CSS

**Complexity**: S

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/index.html`
- Modify: `src/FamilyHQ.WebUi/_Imports.razor`
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

### Step 1.1 — Cut the feature branch

```bash
git checkout dev
git pull origin dev
git checkout -b feature/kiosk-ui-redesign
```

- [ ] **Step 1.1: Cut `feature/kiosk-ui-redesign` from `dev`**

### Step 1.2 — Add Tailwind Play CDN to `index.html`

In `src/FamilyHQ.WebUi/wwwroot/index.html`, inside `<head>`, add after the existing `<title>` tag:

```html
<!-- Tailwind CSS Play CDN -->
<script src="https://cdn.tailwindcss.com"></script>
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
<!-- Burn-in orbit JS interop -->
<script>
  window.setAppTransform = (x, y) => {
    const app = document.getElementById('app');
    app.style.transform = `translate(${x}px, ${y}px)`;
    app.style.transition = 'transform 2s ease-in-out';
  };
</script>
```

- [ ] **Step 1.2: Add Tailwind Play CDN + config + setAppTransform to `index.html`**

### Step 1.3 — Remove Bootstrap references

In `src/FamilyHQ.WebUi/wwwroot/index.html`, remove the Bootstrap CSS `<link>` tag.

In `src/FamilyHQ.WebUi/_Imports.razor`, remove any Bootstrap-related `@using` directives.

- [ ] **Step 1.3: Remove Bootstrap CSS link from `index.html` and Bootstrap usings from `_Imports.razor`**

### Step 1.4 — Strip `app.css` to Blazor error UI only

Replace the contents of `src/FamilyHQ.WebUi/wwwroot/css/app.css` with only the Blazor error UI styles and loading spinner. All Bootstrap-dependent styles are removed. Circadian gradient and weather overlay keyframes will be added in Phase 3.

- [ ] **Step 1.4: Strip `app.css` to Blazor error UI + loading spinner only**

### Step 1.5 — Verify build passes

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 1.5: Confirm build passes**

### Step 1.6 — Commit

```bash
git add src/FamilyHQ.WebUi/wwwroot/index.html
git add src/FamilyHQ.WebUi/_Imports.razor
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(kiosk): add Tailwind Play CDN, remove Bootstrap, add setAppTransform JS interop"
```

- [ ] **Step 1.6: Commit**

**Acceptance criteria:**
- `dotnet build` passes with 0 errors
- `index.html` contains Tailwind CDN script and `tailwind.config` block
- No Bootstrap CSS link in `index.html`
- `setAppTransform` JS function defined in `index.html`

---

## Phase 2 — Kiosk Layout Shell

**Complexity**: M

**Files:**
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor`
- Modify: `src/FamilyHQ.WebUi/Layout/MainLayout.razor.css`
- Delete: `src/FamilyHQ.WebUi/Layout/NavMenu.razor`
- Delete: `src/FamilyHQ.WebUi/Layout/NavMenu.razor.css`
- Create: `src/FamilyHQ.WebUi/Components/DashboardHeader.razor`
- Create: `src/FamilyHQ.WebUi/Components/DashboardHeader.razor.css`
- Create: `src/FamilyHQ.WebUi/Components/MonthQuickJumpModal.razor`
- Create: `src/FamilyHQ.WebUi/Components/BurnInOrbit.razor`

### Step 2.1 — Delete NavMenu

Delete `src/FamilyHQ.WebUi/Layout/NavMenu.razor` and `src/FamilyHQ.WebUi/Layout/NavMenu.razor.css`.

- [ ] **Step 2.1: Delete `NavMenu.razor` and `NavMenu.razor.css`**

### Step 2.2 — Rebuild `MainLayout.razor`

Replace `src/FamilyHQ.WebUi/Layout/MainLayout.razor` with a full-bleed kiosk shell:

```razor
@inherits LayoutComponentBase

<div class="kiosk-root">
    <BurnInOrbit />
    @Body
</div>
```

Update `src/FamilyHQ.WebUi/Layout/MainLayout.razor.css`:

```css
.kiosk-root {
    position: fixed;
    inset: 0;
    width: 1080px;
    height: 1920px;
    overflow: hidden;
    background: transparent;
}
```

- [ ] **Step 2.2: Rebuild `MainLayout.razor` as full-bleed kiosk shell**

### Step 2.3 — Create `BurnInOrbit.razor`

Create `src/FamilyHQ.WebUi/Components/BurnInOrbit.razor`:

```razor
@inject IJSRuntime JSRuntime
@implements IDisposable
@code {
    private static readonly (int X, int Y)[] OrbitPositions = [
        (0, 0), (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1)
    ];
    private int _orbitIndex = 0;
    private Timer? _timer;

    protected override async Task OnInitializedAsync()
    {
        _timer = new Timer(async _ =>
        {
            _orbitIndex = (_orbitIndex + 1) % OrbitPositions.Length;
            var (x, y) = OrbitPositions[_orbitIndex];
            await JSRuntime.InvokeVoidAsync("setAppTransform", x, y);
        }, null, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));
        await JSRuntime.InvokeVoidAsync("setAppTransform", 0, 0);
    }

    public void Dispose() => _timer?.Dispose();
}
```

- [ ] **Step 2.3: Create `BurnInOrbit.razor`**

### Step 2.4 — Create `DashboardHeader.razor`

Create `src/FamilyHQ.WebUi/Components/DashboardHeader.razor` with:
- Parameters: `Year`, `Month`, `SelectedDate`, `ActiveView` (enum `DashboardView`), `Weather` (`WeatherState?`), callbacks for year/month/date/view/settings changes
- Layout: `[◄ Year ►] [◄ Month Name ►] [Today] [Week] [Month] [☁ Weather] [⚙]`
- All buttons minimum 64×64px touch targets
- Month name click opens `MonthQuickJumpModal`
- View switcher: active = `bg-kiosk-accent text-white`, inactive = `bg-kiosk-surface text-kiosk-muted`
- Fixed 120px height, full-width, `z-index: 20`

Add `DashboardView` enum (in same file or separate `DashboardView.cs`):
```csharp
public enum DashboardView { Today, Week, Month }
```

- [ ] **Step 2.4: Create `DashboardHeader.razor` with view switcher and navigation**

### Step 2.5 — Create `MonthQuickJumpModal.razor`

Create `src/FamilyHQ.WebUi/Components/MonthQuickJumpModal.razor`:
- Full-screen overlay (`position: fixed; inset: 0; z-index: 100`)
- Year navigation with `◄`/`►` buttons
- 4×3 grid of month buttons (Jan–Dec), each 200×80px
- Currently selected month highlighted with `bg-kiosk-accent`
- Tapping a month emits `OnDateSelected(year, month)` and closes

- [ ] **Step 2.5: Create `MonthQuickJumpModal.razor`**

### Step 2.6 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Layout/
git add src/FamilyHQ.WebUi/Components/DashboardHeader.razor
git add src/FamilyHQ.WebUi/Components/DashboardHeader.razor.css
git add src/FamilyHQ.WebUi/Components/MonthQuickJumpModal.razor
git add src/FamilyHQ.WebUi/Components/BurnInOrbit.razor
git commit -m "feat(kiosk): rebuild MainLayout as kiosk shell, add DashboardHeader, MonthQuickJumpModal, BurnInOrbit"
```

- [ ] **Step 2.6: Build and commit**

**Acceptance criteria:**
- `dotnet build` passes with 0 errors
- `NavMenu.razor` and `NavMenu.razor.css` are deleted
- `MainLayout.razor` uses `position: fixed; inset: 0` layout
- `BurnInOrbit` calls `setAppTransform` every 120 seconds
- `DashboardHeader` renders all navigation controls with 64px minimum touch targets

---

## Phase 3 — Ambient Background System

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/AmbientBackground.razor`
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

### Step 3.1 — Add circadian gradient CSS to `app.css`

Add to `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* Ambient background layers */
.ambient-gradient {
    position: fixed;
    inset: 0;
    z-index: 0;
    transition: background 3s ease-in-out;
    will-change: transform;
}

.ambient-dawn  { background: linear-gradient(180deg, #1a0533 0%, #7b2d8b 40%, #e8834a 80%, #f5c842 100%); }
.ambient-day   { background: linear-gradient(180deg, #0f4c8a 0%, #1a7fd4 40%, #5bb8f5 80%, #e8f4fd 100%); }
.ambient-dusk  { background: linear-gradient(180deg, #0d1b2a 0%, #1e3a5f 30%, #c0392b 65%, #e67e22 85%, #f39c12 100%); }
.ambient-night { background: linear-gradient(180deg, #020408 0%, #0a0f1e 40%, #0d1b2a 100%); }

/* Weather overlay layer */
.ambient-weather {
    position: fixed;
    inset: 0;
    z-index: 1;
    pointer-events: none;
    will-change: transform;
}

.weather-clear      { opacity: 0; }
.weather-cloudy     { /* slow-drifting semi-transparent grey ellipses */ }
.weather-light-rain { /* diagonal streaks, subtle */ opacity: 0.35; animation: rain-fall 0.8s linear infinite; }
.weather-heavy-rain { /* diagonal streaks, stronger */ opacity: 0.65; filter: blur(1px); animation: rain-fall 0.5s linear infinite; }
.weather-thunder    { animation: thunder-flash 8s ease-in-out infinite; }
.weather-snow       { animation: snow-drift 6s linear infinite; }
.weather-windmist   { opacity: 0.15; animation: mist-sweep 1.2s linear infinite; }

.ambient-weather.no-animate { animation: none !important; opacity: 0 !important; }

/* Legibility layer */
.legibility-layer {
    position: fixed;
    inset: 0;
    z-index: 5;
    background-color: rgba(2, 6, 23, 0.85);
}

/* Weather keyframes */
@keyframes rain-fall {
    from { background-position: 0 0; }
    to   { background-position: -20px 40px; }
}
@keyframes thunder-flash {
    0%, 90%, 100% { opacity: 0; }
    92%, 96%      { opacity: 0.6; background: white; }
}
@keyframes snow-drift {
    from { background-position: 0 0; }
    to   { background-position: 30px 60px; }
}
@keyframes mist-sweep {
    from { background-position: 0 0; }
    to   { background-position: 200px 0; }
}
```

- [ ] **Step 3.1: Add circadian gradient and weather overlay CSS to `app.css`**

### Step 3.2 — Create `AmbientBackground.razor`

Create `src/FamilyHQ.WebUi/Components/AmbientBackground.razor`:

```razor
@implements IDisposable
@code {
    [Parameter] public WeatherState? Weather { get; set; }
    [Parameter] public bool AnimationsEnabled { get; set; } = true;

    private string _circadianClass = "ambient-night";
    private string _weatherClass = "weather-clear";
    private Timer? _timer;

    protected override void OnInitialized()
    {
        UpdateState();
        _timer = new Timer(_ => InvokeAsync(() => { UpdateState(); StateHasChanged(); }),
            null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    protected override void OnParametersSet() => UpdateState();

    private void UpdateState()
    {
        // Circadian class is updated via polling GET /api/circadian/current (every 5 min)
        // See Phase 8b for CircadianStateService
        _weatherClass = Weather?.Condition switch {
            WeatherCondition.Cloudy    => "weather-cloudy",
            WeatherCondition.LightRain => "weather-light-rain",
            WeatherCondition.HeavyRain => "weather-heavy-rain",
            WeatherCondition.Thunder   => "weather-thunder",
            WeatherCondition.Snow      => "weather-snow",
            WeatherCondition.WindMist  => "weather-windmist",
            _                          => "weather-clear"
        };
    }

    public void Dispose() => _timer?.Dispose();
}

<div class="ambient-gradient @_circadianClass"></div>
<div class="ambient-weather @_weatherClass @(AnimationsEnabled ? "" : "no-animate")"></div>
<div class="legibility-layer"></div>
```

- [ ] **Step 3.2: Create `AmbientBackground.razor`**

### Step 3.3 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/AmbientBackground.razor
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(kiosk): add AmbientBackground component with circadian gradients and weather overlays"
```

- [ ] **Step 3.3: Build and commit**

**Acceptance criteria:**
- `AmbientBackground` renders two fixed layers (gradient + weather overlay) + legibility layer
- Circadian state transitions correctly based on `DateTime.Now.Hour`
- Weather CSS class updates when `Weather` parameter changes
- `no-animate` class disables weather animations when `AnimationsEnabled = false`
- No `backdrop-filter: blur` used anywhere

---

## Phase 4 — Today View

**Complexity**: L

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/TodayView.razor`
- Create: `src/FamilyHQ.WebUi/Components/TodayView.razor.css`

### Step 4.1 — Create `TodayView.razor`

Create `src/FamilyHQ.WebUi/Components/TodayView.razor` with:

**Parameters:**
```csharp
[Parameter] public DateOnly Date { get; set; }
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = [];
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; } = [];
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId, TimeOnly Time)> OnSlotTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
```

**Layout:**
- 72px wide time axis (left column), hour labels right-aligned, 14px `text-kiosk-muted`
- Sticky header row: calendar names
- All-day row (80px)
- 24-hour body: 1600px total (`1800 - 120 header - 80 all-day`), `overflow-y: hidden`
- Event tiles: `position: absolute`, `top = (startMinute / 1440) * 100%`, `height = (durationMinutes / 1440) * 100%`, minimum 64px
- Real-time red line: only when `Date == DateOnly.FromDateTime(DateTime.Today)`, updated every 60s via `Timer`
- Touch: tapping empty slot emits `OnSlotTapped` snapped to nearest 15-min boundary

- [ ] **Step 4.1: Create `TodayView.razor` with 24-hour grid, event tiles, and real-time red line**

### Step 4.2 — Wire to `ICalendarApiService`

`TodayView` receives events via `[Parameter]`. The parent (`Index.razor`) calls `ICalendarApiService.GetEventsForRangeAsync` to load today's events and passes them down.

- [ ] **Step 4.2: Confirm TodayView receives events via parameter (no direct API calls in component)**

### Step 4.3 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/TodayView.razor
git add src/FamilyHQ.WebUi/Components/TodayView.razor.css
git commit -m "feat(kiosk): add TodayView component with 24-hour grid, event tiles, real-time indicator"
```

- [ ] **Step 4.3: Build and commit**

**Acceptance criteria:**
- 24-hour vertical grid renders without scrolling in 1800px height
- Event tiles positioned by start/end time with minimum 64px height
- Red line indicator shown only for today, updates every 60 seconds
- All touch targets minimum 64×64px
- Overlapping events split column width 50/50

---

## Phase 5 — Week View

**Complexity**: L

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/WeekView.razor`
- Create: `src/FamilyHQ.WebUi/Components/WeekView.razor.css`

### Step 5.1 — Create `WeekView.razor`

Create `src/FamilyHQ.WebUi/Components/WeekView.razor` with:

**Parameters:**
```csharp
[Parameter] public DateOnly WeekStart { get; set; }  // Monday of the week
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = [];
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; } = [];
[Parameter] public EventCallback<(DateOnly Date, TimeOnly Time)> OnSlotTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
```

**Layout:**
- 72px time axis + 7 day columns, each `(1080 - 72) / 7 ≈ 144px`
- Sticky header row: day names + dates (Mon 27, Tue 28, etc.)
- All-day row (80px)
- 24-hour body: 1600px, `overflow-y: hidden`
- Today column: `bg-kiosk-today border-t-4 border-kiosk-todayBorder`
- Weekend columns: `bg-kiosk-weekend`
- Event tiles: same positioning logic as `TodayView`
- `WeekStart` = Monday of the week containing `SelectedDate` (resolved decision Q6)

- [ ] **Step 5.1: Create `WeekView.razor` with 7-day grid and event tiles**

### Step 5.2 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/WeekView.razor
git add src/FamilyHQ.WebUi/Components/WeekView.razor.css
git commit -m "feat(kiosk): add WeekView component with 7-day hybrid grid"
```

- [ ] **Step 5.2: Build and commit**

**Acceptance criteria:**
- 7-day grid renders without scrolling in 1800px height
- Today column highlighted; weekend columns styled differently
- Event tiles use same positioning logic as TodayView
- `WeekStart` is the Monday of the week containing the selected date

---

## Phase 6 — Month View Redesign

**Complexity**: L

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/CalendarGrid.razor`
- Create: `src/FamilyHQ.WebUi/Components/CalendarGrid.razor.css`
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor` (partial — month section)

### Step 6.1 — Create `CalendarGrid.razor`

Create `src/FamilyHQ.WebUi/Components/CalendarGrid.razor` with:

**Parameters:**
```csharp
[Parameter] public int Year { get; set; }
[Parameter] public int Month { get; set; }
[Parameter] public IReadOnlyList<CalendarSummaryViewModel> Calendars { get; set; } = [];
[Parameter] public IReadOnlyList<CalendarEventViewModel> Events { get; set; } = [];
[Parameter] public int EventDensity { get; set; } = 2;
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId)> OnCellTapped { get; set; }
[Parameter] public EventCallback<CalendarEventViewModel> OnEventTapped { get; set; }
[Parameter] public EventCallback<(DateOnly Date, Guid CalendarId)> OnOverflowTapped { get; set; }
```

**Layout:**
- 31 rows × N calendar columns
- Row height: `floor((1800 - 48 - 16) / 31) = 56px`
- Sticky 96px date column: `position: sticky; left: 0; z-index: 5`; format `"d ddd"` (e.g. `"27 Fri"`)
- Column width: `(1080 - 96) / N` where N = visible calendars
- Today row: `bg-kiosk-today border-l-8 border-kiosk-todayBorder`
- Weekend rows: `bg-kiosk-weekend`
- Days outside current month: `opacity-30`
- Event capsules: `{HH:mm} {Title}`, max `EventDensity` per cell
- `+N` overflow badge: `text-kiosk-accent font-bold`, emits `OnOverflowTapped`

- [ ] **Step 6.1: Create `CalendarGrid.razor` with 31-row vertical list**

### Step 6.2 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/CalendarGrid.razor
git add src/FamilyHQ.WebUi/Components/CalendarGrid.razor.css
git commit -m "feat(kiosk): add CalendarGrid month view with 31-row vertical list and sticky date column"
```

- [ ] **Step 6.2: Build and commit**

**Acceptance criteria:**
- 31 rows fit within 1800px without scrolling
- Sticky date column always visible (never configurable — resolved decision Q5)
- `EventDensity` controls max events per cell (1–3)
- `+N` badge shown when events exceed density limit
- Today and weekend rows styled correctly

---

## Phase 7 — Event Modals

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/EventModal.razor`
- Create: `src/FamilyHQ.WebUi/Components/RecurrenceEditPrompt.razor`
- Create: `src/FamilyHQ.WebUi/Components/DeleteConfirmModal.razor`
- Create: `src/FamilyHQ.WebUi/Components/DayDetailModal.razor`

### Step 7.1 — Create `EventModal.razor`

Create `src/FamilyHQ.WebUi/Components/EventModal.razor` with:
- Full-screen modal (`position: fixed; inset: 0; z-index: 200`)
- Fields: Title, All Day toggle, Start/End date+time, Location, Description, Calendar chips, Recurrence picker
- Recurrence picker: None / Daily / Weekly / Monthly / Custom dropdown
- Weekly: day-of-week checkboxes
- Custom: free-text RRULE input
- Edit mode: shows Delete button; if recurring, triggers `RecurrenceEditPrompt` before save/delete
- All inputs minimum 64px tall

- [ ] **Step 7.1: Create `EventModal.razor` with RRULE recurrence picker**

### Step 7.2 — Create `RecurrenceEditPrompt.razor`

Create `src/FamilyHQ.WebUi/Components/RecurrenceEditPrompt.razor`:
- Three large buttons (minimum 200×80px each):
  1. "This event only" → emits `OnConfirmed(ThisInstance)` — creates an exception instance
  2. "This and following events" → emits `OnConfirmed(ThisAndFollowing)` — splits the series from this date
  3. "All events in series" → emits `OnConfirmed(AllInSeries)` — modifies the master event
- "Cancel" button
- `RecurrenceEditScope` enum: `ThisInstance`, `ThisAndFollowing`, `AllInSeries`
- Same three options apply for delete: "This event only" / "This and following" / "All events in series"

- [ ] **Step 7.2: Create `RecurrenceEditPrompt.razor` with three-option prompt**

### Step 7.3 — Create `DeleteConfirmModal.razor`

Create `src/FamilyHQ.WebUi/Components/DeleteConfirmModal.razor`:
- Centered modal with event title
- "Cancel" (secondary) and "Delete" (red/danger) buttons, each minimum 160×64px

- [ ] **Step 7.3: Create `DeleteConfirmModal.razor`**

### Step 7.4 — Create `DayDetailModal.razor`

Create `src/FamilyHQ.WebUi/Components/DayDetailModal.razor`:
- Full-screen overlay
- Header: date + calendar name
- Scrollable list of all events for that day/calendar
- Each event row: title, time, edit/delete actions
- "Add Event" button at bottom

- [ ] **Step 7.4: Create `DayDetailModal.razor`**

### Step 7.5 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/EventModal.razor
git add src/FamilyHQ.WebUi/Components/RecurrenceEditPrompt.razor
git add src/FamilyHQ.WebUi/Components/DeleteConfirmModal.razor
git add src/FamilyHQ.WebUi/Components/DayDetailModal.razor
git commit -m "feat(kiosk): add EventModal with RRULE picker, RecurrenceEditPrompt, DeleteConfirmModal, DayDetailModal"
```

- [ ] **Step 7.5: Build and commit**

**Acceptance criteria:**
- `EventModal` supports create and edit modes
- RRULE picker handles None/Daily/Weekly/Monthly/Custom
- Recurring event edit/delete triggers `RecurrenceEditPrompt`
- All touch targets minimum 64×64px
- Modals are full-screen on kiosk

---

## Phase 8 — Backend: Weather Service

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs`
- Create: `src/FamilyHQ.Core/Models/WeatherCondition.cs`
- Create: `src/FamilyHQ.Core/Models/WeatherState.cs`
- Create: `src/FamilyHQ.Core/DTOs/WeatherStateDto.cs`
- Create: `src/FamilyHQ.Services/Weather/NullWeatherProvider.cs`
- Create: `src/FamilyHQ.Services/Weather/WeatherBackgroundService.cs`
- Create: `src/FamilyHQ.WebApi/Hubs/WeatherHub.cs`
- Modify: `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`
- Modify: `src/FamilyHQ.WebApi/Program.cs`
- Create: `src/FamilyHQ.WebUi/ViewModels/WeatherStateViewModel.cs`

### Step 8.1 — Add Core types

Create `src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs`:
```csharp
namespace FamilyHQ.Core.Interfaces;
public interface IWeatherProvider
{
    Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken ct = default);
}
```

Create `src/FamilyHQ.Core/Models/WeatherCondition.cs`:
```csharp
namespace FamilyHQ.Core.Models;
public enum WeatherCondition { Clear, Cloudy, LightRain, HeavyRain, Thunder, Snow, WindMist }
```

Create `src/FamilyHQ.Core/Models/WeatherState.cs`:
```csharp
namespace FamilyHQ.Core.Models;
public record WeatherState(WeatherCondition Condition, double? TemperatureCelsius, DateTimeOffset LastUpdated);
```

Create `src/FamilyHQ.Core/DTOs/WeatherStateDto.cs`:
```csharp
namespace FamilyHQ.Core.DTOs;
public record WeatherStateDto(string Condition, double? TemperatureCelsius, DateTimeOffset LastUpdated);
```

- [ ] **Step 8.1: Create Core weather types (IWeatherProvider, WeatherCondition, WeatherState, WeatherStateDto)**

### Step 8.2 — Add `NullWeatherProvider`

Create `src/FamilyHQ.Services/Weather/NullWeatherProvider.cs`:
```csharp
namespace FamilyHQ.Services.Weather;
public class NullWeatherProvider : IWeatherProvider
{
    public Task<WeatherState?> GetCurrentWeatherAsync(CancellationToken ct = default)
        => Task.FromResult<WeatherState?>(null);
}
```

- [ ] **Step 8.2: Create `NullWeatherProvider`**

### Step 8.3 — Add `WeatherBackgroundService`

Create `src/FamilyHQ.Services/Weather/WeatherBackgroundService.cs`:
- Implements `BackgroundService`
- Polls `IWeatherProvider` every 15 minutes
- Caches latest `WeatherState` in thread-safe field
- On each successful poll, calls `IHubContext<WeatherHub>.Clients.All.SendAsync("WeatherUpdate", dto)`
- Exposes `WeatherState? Current { get; }` for initial HTTP fetch

- [ ] **Step 8.3: Create `WeatherBackgroundService`**

### Step 8.4 — Add `WeatherHub`

Create `src/FamilyHQ.WebApi/Hubs/WeatherHub.cs`:
```csharp
namespace FamilyHQ.WebApi.Hubs;
public class WeatherHub : Hub { }
```

- [ ] **Step 8.4: Create `WeatherHub`**

### Step 8.5 — Register services and map hub

In `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`, register:
```csharp
services.AddSingleton<IWeatherProvider, NullWeatherProvider>();
services.AddHostedService<WeatherBackgroundService>();
```

In `src/FamilyHQ.WebApi/Program.cs`, add:
```csharp
app.MapHub<WeatherHub>("/hubs/weather");
```

- [ ] **Step 8.5: Register weather services and map WeatherHub**

### Step 8.6 — Add frontend weather ViewModel and wire SignalR

Create `src/FamilyHQ.WebUi/ViewModels/WeatherStateViewModel.cs`.

Update `src/FamilyHQ.WebUi/Services/SignalRService.cs` to subscribe to `"WeatherUpdate"` messages from `/hubs/weather` and expose a `WeatherStateChanged` event.

- [ ] **Step 8.6: Add WeatherStateViewModel and wire SignalR in frontend**

### Step 8.7 — Build and commit

```bash
dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.Core/Interfaces/IWeatherProvider.cs
git add src/FamilyHQ.Core/Models/WeatherCondition.cs
git add src/FamilyHQ.Core/Models/WeatherState.cs
git add src/FamilyHQ.Core/DTOs/WeatherStateDto.cs
git add src/FamilyHQ.Services/Weather/
git add src/FamilyHQ.WebApi/Hubs/WeatherHub.cs
git add src/FamilyHQ.WebUi/ViewModels/WeatherStateViewModel.cs
git add src/FamilyHQ.Services/ServiceCollectionExtensions.cs
git add src/FamilyHQ.WebApi/Program.cs
git add src/FamilyHQ.WebUi/Services/SignalRService.cs
git commit -m "feat(weather): add IWeatherProvider, NullWeatherProvider, WeatherBackgroundService, WeatherHub SignalR"
```

- [ ] **Step 8.7: Build and commit**

### Step 8.8 — Trigger and verify FamilyHQ-Dev pipeline

- [ ] **Step 8.8: Trigger FamilyHQ-Dev pipeline and confirm it passes**

### Step 8.9 — Add Simulator weather mock endpoint

Create `tools/FamilyHQ.Simulator/Controllers/WeatherController.cs`:
- `POST /api/simulator/weather` — accepts `{ "condition": "HeavyRain", "temperatureCelsius": 12.5 }`, stores in memory
- `GET /api/simulator/weather` — returns current mock weather state
- In-memory state initialised to `{ Condition: "Clear", TemperatureCelsius: null }` on startup

- [ ] **Step 8.9: Create simulator WeatherController with POST and GET /api/simulator/weather**

### Step 8.10 — Add `SimulatorWeatherProvider`

Create `src/FamilyHQ.Services/Weather/SimulatorWeatherProvider.cs`:
- Implements `IWeatherProvider`
- Calls `GET /api/simulator/weather` on the simulator base URL (configured via `SimulatorOptions:BaseUrl`)
- Register conditionally: when `ASPNETCORE_ENVIRONMENT` is `Development` or `Testing`, use `SimulatorWeatherProvider` instead of `NullWeatherProvider`

- [ ] **Step 8.10: Create SimulatorWeatherProvider and register conditionally for dev/test**

### Step 8.11 — Build and commit (simulator + SimulatorWeatherProvider)

```bash
dotnet build tools/FamilyHQ.Simulator/FamilyHQ.Simulator.csproj
dotnet build src/FamilyHQ.Services/FamilyHQ.Services.csproj
```

```bash
git add tools/FamilyHQ.Simulator/Controllers/WeatherController.cs
git add src/FamilyHQ.Services/Weather/SimulatorWeatherProvider.cs
git add src/FamilyHQ.Services/ServiceCollectionExtensions.cs
git commit -m "feat(weather): add simulator weather mock endpoint and SimulatorWeatherProvider"
```

- [ ] **Step 8.11: Build and commit simulator weather endpoint and SimulatorWeatherProvider**

**Acceptance criteria:**
- `IWeatherProvider` interface defined in `FamilyHQ.Core`
- `NullWeatherProvider` returns `null` (no weather data)
- `WeatherBackgroundService` polls every 15 minutes and pushes via SignalR
- `WeatherHub` mapped at `/hubs/weather`
- Frontend `SignalRService` subscribes to `"WeatherUpdate"` messages
- `AmbientBackground` updates CSS class when weather state changes
- Simulator `POST /api/simulator/weather` stores mock weather state in memory
- Simulator `GET /api/simulator/weather` returns current mock weather state
- `SimulatorWeatherProvider` registered in dev/test environments
- FamilyHQ-Dev pipeline passes (build + E2E)

---

## Phase 8b — Dynamic Circadian Service

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.Core/Interfaces/ISolarCalculator.cs`
- Create: `src/FamilyHQ.Core/Models/CircadianBoundaries.cs`
- Create: `src/FamilyHQ.Services/Circadian/SolarCalculator.cs`
- Create: `src/FamilyHQ.Services/Circadian/CircadianStateService.cs`
- Create: `src/FamilyHQ.Services/Options/KioskOptions.cs`
- Create: `src/FamilyHQ.WebApi/Controllers/CircadianController.cs`
- Create: `src/FamilyHQ.Data/Configurations/CircadianBoundariesConfiguration.cs`
- Create: `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddCircadianBoundaries.cs`
- Modify: `src/FamilyHQ.Data/FamilyHqDbContext.cs`
- Modify: `src/FamilyHQ.WebUi/Components/AmbientBackground.razor`
- Modify: `src/FamilyHQ.WebApi/appsettings.json`
- Create: `tests/FamilyHQ.Services.Tests/Circadian/SolarCalculatorTests.cs`

### Step 8b.1 — Write failing unit tests for `SolarCalculator`

Create `tests/FamilyHQ.Services.Tests/Circadian/SolarCalculatorTests.cs`:
- Known sunrise/sunset for London (51.5074°N, -0.1278°W) on 2024-06-21 (summer solstice): sunrise ~04:43 UTC, sunset ~21:21 UTC
- Known sunrise/sunset for London on 2024-12-21 (winter solstice): sunrise ~08:03 UTC, sunset ~15:53 UTC
- Verify DawnStart = Sunrise - 30 min, DuskEnd = Sunset + 30 min

- [ ] **Step 8b.1: Write failing SolarCalculator unit tests with known sunrise/sunset dates**

### Step 8b.2 — Create `ISolarCalculator` and `SolarCalculator`

Create `src/FamilyHQ.Core/Interfaces/ISolarCalculator.cs`:
```csharp
namespace FamilyHQ.Core.Interfaces;
public interface ISolarCalculator
{
    (DateTimeOffset Sunrise, DateTimeOffset Sunset) Calculate(DateOnly date, double latitude, double longitude);
}
```

Create `src/FamilyHQ.Services/Circadian/SolarCalculator.cs`:
- Implements `ISolarCalculator` using the NOAA solar algorithm (pure math, no external API)
- Returns sunrise and sunset as `DateTimeOffset` in UTC

- [ ] **Step 8b.2: Create ISolarCalculator interface and SolarCalculator NOAA implementation**

### Step 8b.3 — Run `SolarCalculator` tests

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~SolarCalculatorTests"
```

Expected: all tests pass.

- [ ] **Step 8b.3: Confirm all SolarCalculator tests pass**

### Step 8b.4 — Create `CircadianBoundaries` model and EF config

Create `src/FamilyHQ.Core/Models/CircadianBoundaries.cs`:
```csharp
namespace FamilyHQ.Core.Models;
public class CircadianBoundaries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public DateTimeOffset Sunrise { get; set; }
    public DateTimeOffset Sunset { get; set; }
    public DateTimeOffset DawnStart { get; set; }
    public DateTimeOffset DuskEnd { get; set; }
}
```

Create `src/FamilyHQ.Data/Configurations/CircadianBoundariesConfiguration.cs`.

Add `DbSet<CircadianBoundaries>` to `src/FamilyHQ.Data/FamilyHqDbContext.cs`.

Generate migration:
```bash
dotnet ef migrations add AddCircadianBoundaries --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```

- [ ] **Step 8b.4: Create CircadianBoundaries model, EF config, DbSet, and generate migration**

### Step 8b.5 — Create `KioskOptions` and add to `appsettings.json`

Create `src/FamilyHQ.Services/Options/KioskOptions.cs`:
```csharp
namespace FamilyHQ.Services.Options;
public class KioskOptions
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
```

Add to `src/FamilyHQ.WebApi/appsettings.json`:
```json
"Kiosk": {
  "Latitude": 51.5074,
  "Longitude": -0.1278
}
```

- [ ] **Step 8b.5: Create KioskOptions and add Kiosk:Latitude/Longitude to appsettings.json**

### Step 8b.6 — Create `CircadianStateService`

Create `src/FamilyHQ.Services/Circadian/CircadianStateService.cs`:
- Implements `BackgroundService`
- On startup and daily at midnight: calls `ISolarCalculator.Calculate` with configured lat/lon, stores result in `CircadianBoundaries` table
- Exposes `CircadianState GetCurrentState()` — computes current state from today's stored boundaries:
  - **Dawn**: `DawnStart` ≤ now < `Sunrise`
  - **Day**: `Sunrise` ≤ now < `Sunset`
  - **Dusk**: `Sunset` ≤ now < `DuskEnd`
  - **Night**: all other times

- [ ] **Step 8b.6: Create CircadianStateService background service**

### Step 8b.7 — Create `CircadianController`

Create `src/FamilyHQ.WebApi/Controllers/CircadianController.cs`:
```
GET /api/circadian/current  → returns { "state": "Day" | "Dawn" | "Dusk" | "Night" }
```

Register `CircadianStateService` as a hosted service in `src/FamilyHQ.Services/ServiceCollectionExtensions.cs`.

- [ ] **Step 8b.7: Create CircadianController with GET /api/circadian/current**

### Step 8b.8 — Update `AmbientBackground.razor` to poll circadian endpoint

Update `src/FamilyHQ.WebUi/Components/AmbientBackground.razor`:
- Remove the local clock-based circadian state computation
- Add a 5-minute timer that calls `GET /api/circadian/current` and updates `_circadianClass`
- On initial render, call the endpoint immediately

- [ ] **Step 8b.8: Update AmbientBackground.razor to poll /api/circadian/current every 5 minutes**

### Step 8b.9 — Build and commit

```bash
dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.Core/Interfaces/ISolarCalculator.cs
git add src/FamilyHQ.Core/Models/CircadianBoundaries.cs
git add src/FamilyHQ.Services/Circadian/
git add src/FamilyHQ.Services/Options/KioskOptions.cs
git add src/FamilyHQ.WebApi/Controllers/CircadianController.cs
git add src/FamilyHQ.Data/
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git add src/FamilyHQ.WebUi/Components/AmbientBackground.razor
git add src/FamilyHQ.WebApi/appsettings.json
git add tests/FamilyHQ.Services.Tests/Circadian/SolarCalculatorTests.cs
git commit -m "feat(circadian): add dynamic circadian service with NOAA solar algorithm, CircadianController, AmbientBackground polling"
```

- [ ] **Step 8b.9: Build and commit**

### Step 8b.10 — Trigger and verify FamilyHQ-Dev pipeline

- [ ] **Step 8b.10: Trigger FamilyHQ-Dev pipeline and confirm it passes**

**Acceptance criteria:**
- `SolarCalculator` produces correct sunrise/sunset for known dates and locations
- `CircadianBoundaries` table created by EF migration
- `CircadianStateService` computes and stores daily boundaries at midnight
- `GET /api/circadian/current` returns correct state based on stored boundaries
- `AmbientBackground` polls endpoint every 5 minutes and updates CSS class
- `KioskOptions` configured with latitude/longitude in `appsettings.json`
- All `SolarCalculatorTests` pass
- FamilyHQ-Dev pipeline passes (build + E2E)

---

## Phase 9 — Backend: RRULE Recurrence

**Complexity**: XL

**Files:**
- Modify: `src/FamilyHQ.Core/Models/CalendarEvent.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IRruleExpander.cs`
- Modify: `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs`
- Modify: `src/FamilyHQ.Core/DTOs/CreateEventRequest.cs`
- Modify: `src/FamilyHQ.Core/DTOs/UpdateEventRequest.cs`
- Create: `src/FamilyHQ.Services/Calendar/RruleExpander.cs`
- Modify: `src/FamilyHQ.Services/Calendar/CalendarEventService.cs`
- Modify: `src/FamilyHQ.Services/FamilyHQ.Services.csproj`
- Modify: `src/FamilyHQ.WebApi/Controllers/EventsController.cs`
- Modify: `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs`
- Create: `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddRruleFields.cs`
- Modify: `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs`
- Modify: `src/FamilyHQ.WebUi/Services/CalendarApiService.cs`
- Modify: `Directory.Packages.props`

### Step 9.1 — Write failing tests for `RruleExpander`

Add tests to `tests/FamilyHQ.Services.Tests/Calendar/RruleExpanderTests.cs`:
- Daily recurrence expands correctly for a date range
- Weekly recurrence with specific days expands correctly
- Monthly recurrence expands correctly
- Exception instances override generated occurrences
- Events outside the range are excluded

- [ ] **Step 9.1: Write failing tests for `RruleExpander`**

### Step 9.2 — Add RRULE fields to `CalendarEvent`

Modify `src/FamilyHQ.Core/Models/CalendarEvent.cs` — add:
```csharp
public string? RecurrenceRule { get; set; }
public string? RecurrenceId { get; set; }
public bool IsRecurrenceException { get; set; }
```

- [ ] **Step 9.2: Add RecurrenceRule, RecurrenceId, IsRecurrenceException to CalendarEvent**

### Step 9.3 — Add `IRruleExpander` interface

Create `src/FamilyHQ.Core/Interfaces/IRruleExpander.cs`:
```csharp
namespace FamilyHQ.Core.Interfaces;
public interface IRruleExpander
{
    IReadOnlyList<CalendarEvent> Expand(
        CalendarEvent master,
        IReadOnlyList<CalendarEvent> exceptions,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd);
}
```

- [ ] **Step 9.3: Create IRruleExpander interface**

### Step 9.4 — Add `Ical.Net` NuGet package

Modify `src/FamilyHQ.Services/FamilyHQ.Services.csproj` to add `Ical.Net` package reference.
Modify `Directory.Packages.props` to add `Ical.Net` version.

**Requires approval** (new NuGet dependency).

- [ ] **Step 9.4: Add Ical.Net NuGet package to FamilyHQ.Services (requires approval)**

### Step 9.5 — Implement `RruleExpander`

Create `src/FamilyHQ.Services/Calendar/RruleExpander.cs`:
- Uses `Ical.Net` to parse RRULE strings
- For each occurrence date, creates a synthetic `CalendarEvent` with master's properties
- Checks exceptions list; substitutes exception event when matched by `RecurrenceId` and date
- Returns merged list sorted by `Start`

- [ ] **Step 9.5: Implement RruleExpander using Ical.Net**

### Step 9.6 — Run `RruleExpander` tests

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~RruleExpanderTests"
```

Expected: all tests pass.

- [ ] **Step 9.6: Confirm all RruleExpander tests pass**

### Step 9.7 — Update `ICalendarEventService` and `CalendarEventService`

Add to `src/FamilyHQ.Core/Interfaces/ICalendarEventService.cs`:
```csharp
Task<IReadOnlyList<CalendarEvent>> GetEventsForRangeAsync(
    DateTimeOffset start, DateTimeOffset end, CancellationToken ct = default);

// ThisInstance: creates exception row (IsRecurrenceException = true), calls Google events.patch on instance
Task<CalendarEvent> UpdateInstanceAsync(
    Guid eventId, UpdateEventRequest request, CancellationToken ct = default);

// ThisAndFollowing: updates UNTIL in original RRULE, creates new series from this date
// Calls Google Calendar to split the series
Task<CalendarEvent> UpdateSeriesFromAsync(
    Guid eventId, DateTimeOffset fromDate, UpdateEventRequest request, CancellationToken ct = default);

// AllInSeries: updates the master event; calls Google events.patch on the master
Task<CalendarEvent> UpdateAllInSeriesAsync(
    Guid masterId, UpdateEventRequest request, CancellationToken ct = default);

// ThisInstance: calls Google events.delete on the specific instance; adds EXDATE to series
Task DeleteInstanceAsync(Guid eventId, CancellationToken ct = default);

// ThisAndFollowing: truncates series at this date (updates UNTIL in RRULE)
Task DeleteSeriesFromAsync(Guid eventId, DateTimeOffset fromDate, CancellationToken ct = default);

// AllInSeries: deletes the master event and all exceptions
Task DeleteAllInSeriesAsync(Guid masterId, CancellationToken ct = default);
```

Update `src/FamilyHQ.Services/Calendar/CalendarEventService.cs` to implement all these methods.

- [ ] **Step 9.7: Update ICalendarEventService and CalendarEventService with all three recurrence edit scope methods**

### Step 9.7b — Update `CalendarSyncService` for recurrence edit modes

> **Note**: FamilyHQ is Google Calendar-first. Events are created/edited/deleted in Google Calendar on users' phones more often than in the FamilyHQ app. All three recurrence edit modes must sync back to Google Calendar correctly.

Update `src/FamilyHQ.Services/Calendar/CalendarSyncService.cs` to handle:
- **Exception instances** (`IsRecurrenceException = true`): sync as individual event overrides in Google Calendar (Google Calendar API: `events.patch` on the specific instance event ID)
- **Series splits** (`ThisAndFollowing`): update `UNTIL` in the original series RRULE via `events.patch` on the master; create a new series via `events.insert` with the new RRULE starting from the split date
- **Master updates** (`AllInSeries`): `events.patch` on the master event ID
- **Incoming sync from Google**: when Google Calendar pushes a change (via webhook or poll), correctly identify exception instances, series splits, and master updates and apply them to the local DB

- [ ] **Step 9.7b: Update CalendarSyncService to handle exception instances, series splits, and master updates for Google Calendar sync**

### Step 9.8 — Update DTOs

Add `string? RecurrenceRule` to `CreateEventRequest` and `UpdateEventRequest`.

- [ ] **Step 9.8: Add RecurrenceRule field to CreateEventRequest and UpdateEventRequest**

### Step 9.9 — Update `EventsController`

Add `GET /api/events/range?start=&end=` endpoint to `src/FamilyHQ.WebApi/Controllers/EventsController.cs`.

- [ ] **Step 9.9: Add GET /api/events/range endpoint to EventsController**

### Step 9.10 — EF config and migration

Update `src/FamilyHQ.Data/Configurations/CalendarEventConfiguration.cs` to configure the three new columns.

Generate migration:
```bash
dotnet ef migrations add AddRruleFields --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```

- [ ] **Step 9.10: Update CalendarEventConfiguration and generate AddRruleFields migration**

### Step 9.11 — Update frontend API service

Add `GetEventsForRangeAsync` to `src/FamilyHQ.WebUi/Services/ICalendarApiService.cs` and implement in `CalendarApiService.cs`.

- [ ] **Step 9.11: Add GetEventsForRangeAsync to ICalendarApiService and CalendarApiService**

### Step 9.12 — Build and commit

```bash
dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.Core/
git add src/FamilyHQ.Services/Calendar/RruleExpander.cs
git add src/FamilyHQ.Services/Calendar/CalendarEventService.cs
git add src/FamilyHQ.Services/FamilyHQ.Services.csproj
git add src/FamilyHQ.WebApi/Controllers/EventsController.cs
git add src/FamilyHQ.Data/
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git add src/FamilyHQ.WebUi/Services/
git add Directory.Packages.props
git add tests/FamilyHQ.Services.Tests/Calendar/RruleExpanderTests.cs
git commit -m "feat(rrule): add server-side RRULE expansion with Ical.Net, IRruleExpander, GetEventsForRangeAsync endpoint"
```

- [ ] **Step 9.12: Build and commit**

**Acceptance criteria:**
- `RruleExpander` correctly expands Daily/Weekly/Monthly RRULE strings
- Exception instances override generated occurrences
- `GET /api/events/range` returns expanded concrete instances (no RRULE strings in response)
- `CalendarEventService.GetEventsForRangeAsync` merges recurring + non-recurring events
- EF migration adds three columns to `Events` table
- All `RruleExpanderTests` pass

---

## Phase 10 — Backend: User Preferences

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.Core/Models/UserPreferences.cs`
- Create: `src/FamilyHQ.Core/DTOs/UserPreferencesDto.cs`
- Create: `src/FamilyHQ.Core/Interfaces/IUserPreferencesService.cs`
- Create: `src/FamilyHQ.Services/UserPreferencesService.cs`
- Create: `src/FamilyHQ.WebApi/Controllers/PreferencesController.cs`
- Create: `src/FamilyHQ.Data/Configurations/UserPreferencesConfiguration.cs`
- Modify: `src/FamilyHQ.Data/FamilyHqDbContext.cs`
- Create: `src/FamilyHQ.Data.PostgreSQL/Migrations/{ts}_AddUserPreferences.cs`
- Create: `src/FamilyHQ.WebUi/Services/IUserPreferencesApiService.cs`
- Create: `src/FamilyHQ.WebUi/Services/UserPreferencesService.cs`
- Create: `src/FamilyHQ.WebUi/ViewModels/UserPreferencesViewModel.cs`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

### Step 10.1 — Write failing tests for `UserPreferencesService`

Add tests to `tests/FamilyHQ.Services.Tests/UserPreferencesServiceTests.cs`:
- `GetAsync` returns default preferences when none exist
- `GetAsync` returns stored preferences for a user
- `SaveAsync` creates new preferences when none exist
- `SaveAsync` updates existing preferences

- [ ] **Step 10.1: Write failing tests for UserPreferencesService**

### Step 10.2 — Add Core types

Create `src/FamilyHQ.Core/Models/UserPreferences.cs`:
```csharp
namespace FamilyHQ.Core.Models;
public class UserPreferences
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;
    public string? ColumnOrderJson { get; set; }
    public string? HiddenCalendarIdsJson { get; set; }
    public string? CalendarColorsJson { get; set; }
    public int EventDensity { get; set; } = 2;
    public string DefaultView { get; set; } = "Today";
    public bool AnimatedBackground { get; set; } = true;
}
```

Create `src/FamilyHQ.Core/DTOs/UserPreferencesDto.cs`.

Create `src/FamilyHQ.Core/Interfaces/IUserPreferencesService.cs`.

- [ ] **Step 10.2: Create UserPreferences model, DTO, and interface**

### Step 10.3 — Implement `UserPreferencesService` (backend)

Create `src/FamilyHQ.Services/UserPreferencesService.cs` implementing `IUserPreferencesService`.

- [ ] **Step 10.3: Implement backend UserPreferencesService**

### Step 10.4 — Run `UserPreferencesService` tests

```bash
dotnet test tests/FamilyHQ.Services.Tests --filter "FullyQualifiedName~UserPreferencesServiceTests"
```

Expected: all tests pass.

- [ ] **Step 10.4: Confirm all UserPreferencesService tests pass**

### Step 10.5 — EF config, DbContext, and migration

Create `src/FamilyHQ.Data/Configurations/UserPreferencesConfiguration.cs`.

Add `DbSet<UserPreferences>` to `src/FamilyHQ.Data/FamilyHqDbContext.cs`.

Generate migration:
```bash
dotnet ef migrations add AddUserPreferences --project src/FamilyHQ.Data.PostgreSQL --startup-project src/FamilyHQ.WebApi
```

- [ ] **Step 10.5: Add UserPreferencesConfiguration, DbSet, and generate migration**

### Step 10.6 — Add `PreferencesController`

Create `src/FamilyHQ.WebApi/Controllers/PreferencesController.cs`:
- `GET /api/preferences` → returns `UserPreferencesDto` for current user
- `PUT /api/preferences` → accepts `UserPreferencesDto`, saves, returns updated dto

- [ ] **Step 10.6: Create PreferencesController with GET and PUT endpoints**

### Step 10.7 — Add frontend preferences service

Create `src/FamilyHQ.WebUi/Services/IUserPreferencesApiService.cs`.

Create `src/FamilyHQ.WebUi/Services/UserPreferencesService.cs`:
- On startup: loads from `localStorage` first (instant, no flicker)
- Then fetches `GET /api/preferences` and merges (backend wins on conflict)
- On change: writes to `localStorage` immediately, debounces `PUT /api/preferences` by 1 second
- Exposes `UserPreferencesViewModel` as reactive property with `OnChange` event

Create `src/FamilyHQ.WebUi/ViewModels/UserPreferencesViewModel.cs`.

Register in `src/FamilyHQ.WebUi/Program.cs`.

- [ ] **Step 10.7: Add frontend UserPreferencesService with LocalStorage-first + debounced sync**

### Step 10.8 — Build and commit

```bash
dotnet build src/FamilyHQ.WebApi/FamilyHQ.WebApi.csproj
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.Core/Models/UserPreferences.cs
git add src/FamilyHQ.Core/DTOs/UserPreferencesDto.cs
git add src/FamilyHQ.Core/Interfaces/IUserPreferencesService.cs
git add src/FamilyHQ.Services/UserPreferencesService.cs
git add src/FamilyHQ.WebApi/Controllers/PreferencesController.cs
git add src/FamilyHQ.Data/
git add src/FamilyHQ.Data.PostgreSQL/Migrations/
git add src/FamilyHQ.WebUi/Services/IUserPreferencesApiService.cs
git add src/FamilyHQ.WebUi/Services/UserPreferencesService.cs
git add src/FamilyHQ.WebUi/ViewModels/UserPreferencesViewModel.cs
git add src/FamilyHQ.WebUi/Program.cs
git add tests/FamilyHQ.Services.Tests/UserPreferencesServiceTests.cs
git commit -m "feat(preferences): add UserPreferences model, PreferencesController, frontend LocalStorage-first service"
```

- [ ] **Step 10.8: Build and commit**

### Step 10.9 — Trigger and verify FamilyHQ-Dev pipeline

- [ ] **Step 10.9: Trigger FamilyHQ-Dev pipeline and confirm it passes**

**Acceptance criteria:**
- `GET /api/preferences` returns default preferences for new users
- `PUT /api/preferences` persists changes to DB
- Frontend loads from `localStorage` on startup (no flicker)
- Backend sync happens after 1 second debounce
- `UserPreferencesViewModel` exposes `OnChange` event for component subscriptions
- FamilyHQ-Dev pipeline passes (build + E2E)

---

## Phase 11 — Settings Panel

**Complexity**: M

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/SettingsPanel.razor`

### Step 11.1 — Create `SettingsPanel.razor`

Create `src/FamilyHQ.WebUi/Components/SettingsPanel.razor` with four sections:
1. **Column Management**: Up/down buttons for calendar column reordering; visibility toggle per calendar
2. **Color Coding**: Palette of 12 preset colors per calendar
3. **Display Preferences**: Event density (1–3), default view (Today/Week/Month), animated background toggle
4. **Authentication**: Username display, Sign Out button (fixed `bottom-4 right-4`)

Wire all changes to `UserPreferencesService` (immediate `localStorage` write + debounced backend sync).

- [ ] **Step 11.1: Create SettingsPanel.razor with column management, colors, display preferences, and auth**

### Step 11.2 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Components/SettingsPanel.razor
git commit -m "feat(kiosk): add SettingsPanel with column order, color overrides, display preferences"
```

- [ ] **Step 11.2: Build and commit**

### Step 11.3 — Trigger and verify FamilyHQ-Dev pipeline

- [ ] **Step 11.3: Trigger FamilyHQ-Dev pipeline and confirm it passes**

**Acceptance criteria:**
- Column reordering persists via `UserPreferencesService`
- Color changes apply immediately to calendar columns
- Event density change updates `CalendarGrid` immediately
- Animated background toggle disables weather CSS animations
- Sign Out button visible and functional
- FamilyHQ-Dev pipeline passes (build + E2E)

---

## Phase 12 — Wire `Index.razor` Shell

**Complexity**: M

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Index.razor`

### Step 12.1 — Rebuild `Index.razor` as thin shell

Replace `src/FamilyHQ.WebUi/Pages/Index.razor` with a thin shell that:
- Renders `AmbientBackground` (receives `WeatherState` from `SignalRService`)
- Renders `DashboardHeader` (receives year/month/view/weather state; handles navigation callbacks)
- Renders the active view component based on `DashboardView` enum:
  - `DashboardView.Today` → `TodayView`
  - `DashboardView.Week` → `WeekView`
  - `DashboardView.Month` → `CalendarGrid`
- Renders `EventModal`, `RecurrenceEditPrompt`, `DeleteConfirmModal`, `DayDetailModal`, `SettingsPanel` (visibility controlled by state)
- Loads events via `ICalendarApiService` when view or date changes
- Loads preferences via `UserPreferencesService` on startup
- Subscribes to `SignalRService.WeatherStateChanged`

- [ ] **Step 12.1: Rebuild Index.razor as thin shell wiring all components together**

### Step 12.2 — Build and commit

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

```bash
git add src/FamilyHQ.WebUi/Pages/Index.razor
git commit -m "feat(kiosk): rebuild Index.razor as thin shell wiring all kiosk components"
```

- [ ] **Step 12.2: Build and commit**

### Step 12.3 — Update all existing E2E tests for new shell and trigger pipeline

`Index.razor` is now a thin shell — all existing `Dashboard.feature` scenarios that interact with the page must be reviewed. Update `EventSteps.cs`, `UserSteps.cs`, and any other step files to use the new component structure. Trigger the FamilyHQ-Dev pipeline and confirm it passes.

- [ ] **Step 12.3: Review and update all existing E2E tests for new Index.razor shell, trigger FamilyHQ-Dev pipeline**

**Acceptance criteria:**
- All three views render correctly when view switcher is tapped
- Navigation (year/month/week) updates the displayed data
- Weather state from SignalR updates `AmbientBackground` and `DashboardHeader`
- User preferences from `UserPreferencesService` applied to `CalendarGrid` (density, column order)
- Event CRUD operations work end-to-end through modals
- All existing `Dashboard.feature` and `Authentication.feature` scenarios pass
- FamilyHQ-Dev pipeline passes (build + E2E)

---

## Phase 13 — Tests

**Complexity**: L

**Files:**
- Create: `tests/FamilyHQ.Services.Tests/Calendar/RruleExpanderTests.cs` (already created in Phase 9)
- Create: `tests/FamilyHQ.Services.Tests/UserPreferencesServiceTests.cs` (already created in Phase 10)
- Create: `tests/FamilyHQ.Services.Tests/Weather/WeatherBackgroundServiceTests.cs`
- Modify: `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs`
- Create: `tests-e2e/FamilyHQ.E2E.Features/KioskViews.feature`

### Step 13.1 — Add `WeatherBackgroundService` unit tests

Create `tests/FamilyHQ.Services.Tests/Weather/WeatherBackgroundServiceTests.cs`:
- `ExecuteAsync` calls `IWeatherProvider.GetCurrentWeatherAsync` on schedule
- On successful poll, pushes `WeatherUpdate` via `IHubContext<WeatherHub>`
- `Current` property returns latest cached state
- Null result from provider does not push update

- [ ] **Step 13.1: Write WeatherBackgroundService unit tests**

### Step 13.2 — Update `CalendarEventServiceTests` for RRULE

Update `tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs`:
- `GetEventsForRangeAsync` returns non-recurring events in range
- `GetEventsForRangeAsync` expands recurring events via `IRruleExpander`
- `UpdateInstanceAsync` creates exception row with `IsRecurrenceException = true`
- `UpdateSeriesFromAsync` updates UNTIL in original RRULE and creates new series
- `UpdateAllInSeriesAsync` updates the master event
- `DeleteInstanceAsync` removes exception row if present
- `DeleteSeriesFromAsync` truncates series at the given date
- `DeleteAllInSeriesAsync` deletes master and all exceptions

- [ ] **Step 13.2: Update CalendarEventServiceTests for all three recurrence edit scope methods**

### Step 13.3 — Add E2E BDD scenarios

Create `tests-e2e/FamilyHQ.E2E.Features/KioskViews.feature`:

```gherkin
Feature: Kiosk Views

  Scenario: Today View loads with events
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has an all-day event "School Run" today
    When I navigate to the dashboard
    Then the Today View is displayed
    And the event "School Run" is visible

  Scenario: Month View loads without scrolling
    Given I have a user like "TestFamilyMember"
    When I navigate to the dashboard
    And I switch to Month view
    Then the Month View is displayed
    And all 31 rows are visible without scrolling

  Scenario: Create recurring event
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    When I navigate to the dashboard
    And I tap an empty slot on Monday
    And I set the title to "Weekly Meeting"
    And I set recurrence to "Weekly"
    And I save the event
    Then the event "Weekly Meeting" appears on every Monday in the month view

  Scenario: Edit this instance of recurring event
    Given I have a user like "TestFamilyMember"
    And the "Family Events" calendar is the active calendar
    And the user has a recurring weekly event "Team Standup"
    When I tap the event "Team Standup" on a specific day
    And I change the title to "Team Standup - Special"
    And I choose "This event only"
    Then only that instance shows "Team Standup - Special"
    And other instances still show "Team Standup"

  Scenario: Weather HeavyRain shows heavy-rain overlay
    Given the simulator weather is set to "HeavyRain" with temperature 8.0
    When I navigate to the dashboard
    Then the ambient background has the CSS class "weather-heavy-rain"

  Scenario: Weather Clear shows no weather overlay
    Given the simulator weather is set to "Clear" with no temperature
    When I navigate to the dashboard
    Then the ambient background has the CSS class "weather-clear"
```

- [ ] **Step 13.3: Create KioskViews.feature with E2E BDD scenarios including weather state scenarios**

### Step 13.4 — Run all unit tests

```bash
dotnet test tests/FamilyHQ.Services.Tests/
```

Expected: all tests pass.

- [ ] **Step 13.4: Confirm all unit tests pass**

### Step 13.5 — Commit

```bash
git add tests/FamilyHQ.Services.Tests/Weather/WeatherBackgroundServiceTests.cs
git add tests/FamilyHQ.Services.Tests/Calendar/CalendarEventServiceTests.cs
git add tests-e2e/FamilyHQ.E2E.Features/KioskViews.feature
git commit -m "test(kiosk): add WeatherBackgroundService tests, update CalendarEventService tests, add KioskViews E2E feature"
```

- [ ] **Step 13.5: Commit**

**Acceptance criteria:**
- All `WeatherBackgroundServiceTests` pass
- All updated `CalendarEventServiceTests` pass (including all three recurrence edit scope methods)
- `KioskViews.feature` covers Today View, Month View, create recurring event, edit instance vs series, weather state changes
- Weather E2E scenarios: `HeavyRain` → `weather-heavy-rain` CSS class; `Clear` → `weather-clear` CSS class
- Full test suite passes: `dotnet test tests/FamilyHQ.Services.Tests/`

---

## Phase 14 — Git Commit and PR

**Complexity**: S

**Files:** None (git operations only)

### Step 14.1 — Final build verification

```bash
dotnet build FamilyHQ.slnx
dotnet test tests/FamilyHQ.Services.Tests/
dotnet test tests/FamilyHQ.Core.Tests/
```

Expected: all builds succeed, all tests pass.

- [ ] **Step 14.1: Final build and test verification**

### Step 14.2 — Follow git-commit-formatter skill

Read `.agent/skills/git-commit-formatter/SKILL.md` before writing the final commit message.

- [ ] **Step 14.2: Read git-commit-formatter SKILL.md**

### Step 14.3 — Follow git-workflow skill

Read `.agent/skills/git-workflow/SKILL.md` for branch/PR workflow.

- [ ] **Step 14.3: Read git-workflow SKILL.md**

### Step 14.4 — Raise PR

Follow the git-workflow skill to raise a PR from `feature/kiosk-ui-redesign` to `dev`.

- [ ] **Step 14.4: Raise PR from feature/kiosk-ui-redesign to dev**

**Acceptance criteria:**
- All builds pass
- All unit tests pass
- All E2E tests pass on the FamilyHQ-Dev pipeline
- PR raised against `dev` branch
- PR description references design spec `docs/superpowers/specs/2026-03-27-kiosk-ui-redesign-design.md`

---

## Dependency Graph

```
Phase 1 (Foundation)
    └── Phase 2 (Layout Shell)
            └── Phase 3 (Ambient Background)
            └── Phase 4 (Today View)
            └── Phase 5 (Week View)
            └── Phase 6 (Month View)
            └── Phase 7 (Event Modals)
Phase 8 (Weather Backend)
    └── Phase 8b (Dynamic Circadian Service) ──────────────────────┐
Phase 9 (RRULE Backend) ────────────────────────────────────────────┤
Phase 10 (Preferences Backend) ─────────────────────────────────────┤
                                                                     ▼
                                                          Phase 12 (Wire Index.razor)
                                                                     │
Phase 11 (Settings Panel) ──────────────────────────────────────────┘
                                                                     ▼
                                                          Phase 13 (Tests)
                                                                     ▼
                                                          Phase 14 (Git / PR)
```

Phases 2–7 (frontend components) can proceed in parallel with Phases 8–11 (backend services). Phase 8b depends on Phase 8 (weather types must exist). Phase 12 requires all frontend components (2–7) and all backend services (8–11) to be complete.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Tailwind Play CDN unavailable on LAN-only Pi | Medium | High | Cache CDN script locally in `wwwroot/lib/tailwind/`; fall back to local file if CDN unreachable |
| `Ical.Net` RRULE parsing edge cases | Medium | Medium | Unit test all three supported patterns (Daily/Weekly/Monthly); log and skip unparseable RRULE strings |
| 31-row month grid overflows 1920px on some months | Low | High | Row height formula uses `floor()` — verify with 31-day month; add integration test asserting no overflow |
| SignalR connection drops on Pi (LAN instability) | Low | Medium | `SignalRService` already has reconnect logic; weather state falls back to last cached value |
| `WeatherBackgroundService` blocks startup | Low | Low | `BackgroundService` runs on a separate thread; no startup blocking |
| EF migration conflicts with existing data | Low | High | RRULE columns are nullable with defaults; `UserPreferences` is a new table — no data migration needed |
| Bootstrap removal breaks existing styles | Medium | Medium | Strip Bootstrap in Phase 1 before any component work; verify build passes before proceeding |
| Drag-and-drop column reorder unreliable on touch | Medium | Low | Use up/down buttons as primary reorder mechanism (spec already notes this as fallback) |
| `Ical.Net` NuGet approval delay | Low | Medium | Phase 9 is blocked until approved; Phases 1–8 and 10–11 can proceed in parallel |

---

## Complexity Summary

| Phase | Description | Complexity |
|-------|-------------|-----------|
| 1 | Foundation: Git branch + Tailwind CSS | S |
| 2 | Kiosk Layout Shell | M |
| 3 | Ambient Background System | M |
| 4 | Today View | L |
| 5 | Week View | L |
| 6 | Month View Redesign | L |
| 7 | Event Modals | M |
| 8 | Backend: Weather Service + Simulator Mock Endpoint | M |
| 8b | Dynamic Circadian Service (NOAA solar algorithm) | M |
| 9 | Backend: RRULE Recurrence + Google Calendar Sync | XL |
| 10 | Backend: User Preferences | M |
| 11 | Settings Panel | M |
| 12 | Wire Index.razor Shell | M |
| 13 | Tests | L |
| 14 | Git Commit and PR | S |

---

## Done

All 14 phases complete. The branch `feature/kiosk-ui-redesign` is ready for a build pipeline run and E2E test pass before merging to `dev`.
