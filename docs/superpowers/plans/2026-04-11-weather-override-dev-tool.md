# Weather Override Dev Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dev/staging-only Settings tab that lets a developer manually force the full-screen weather animation (`WeatherOverlay`) to any `WeatherCondition`, with an independent Windy modifier, for visual testing. Gated by a feature flag sourced from an environment variable. Purely client-side; backend, API, user settings, and `WeatherStrip` untouched.

**Architecture:** A `FeatureFlags` POCO reads `FeatureWeatherOverride` from `appsettings.json`, which gets its value injected at Docker container startup by the existing `docker-entrypoint.sh` sed pipeline. A scoped `IWeatherOverrideService` holds transient override state and raises an event when it changes. `WeatherOverlay` subscribes and short-circuits its condition-resolution path when the override is active. A new `SettingsWeatherOverrideTab` component, gated in `Settings.razor` by the flag, exposes pill toggles for activate/deactivate, condition selection, and the Windy modifier.

**Tech Stack:** .NET 10, Blazor WebAssembly, xUnit + FluentAssertions (WebUi.Tests), Reqnroll + Playwright (E2E), Docker Compose, nginx-served static WASM bundle.

**Spec:** [`docs/superpowers/specs/2026-04-11-weather-override-dev-tool-design.md`](../specs/2026-04-11-weather-override-dev-tool-design.md)

**Branch:** `feature/weather-override-dev-tool` (already created from `origin/dev`)

---

## Testing Strategy

The WebUi test project (`tests/FamilyHQ.WebUi.Tests`) uses **xUnit + Moq + FluentAssertions only — no bUnit**. Adding bUnit would require approval per `AGENTS.md` and is not necessary here because:

- The `WeatherOverrideService` holds all the real logic and is fully unit-testable with xUnit.
- `SettingsWeatherOverrideTab` and the `WeatherOverlay` modifications are thin shells that delegate to the service, so end-to-end Playwright tests already planned for this feature exercise them directly against a real browser.

**Coverage split:**

- **Unit tests (xUnit):** `WeatherOverrideService` — all 10 behaviours from the spec.
- **E2E tests (Reqnroll + Playwright):** Feature-flag visibility of the tab, activate + condition selection engaging the animation class, Windy modifier adding the `weather-windy` class, and deactivation rolling back to real weather.
- **Manual smoke:** `dotnet run` local check and Docker image sed-substitution check.

---

## File Structure

### New files

| Path | Responsibility |
|------|----------------|
| `src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs` | POCO holding client-side feature flag values read from configuration at app startup. |
| `src/FamilyHQ.WebUi/Services/IWeatherOverrideService.cs` | Interface for transient override state + change event. |
| `src/FamilyHQ.WebUi/Services/WeatherOverrideService.cs` | Plain implementation: three fields, event fires only on real state changes. |
| `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherOverrideTab.razor` | New settings tab UI: activate pill, condition grid, Windy modifier pill. |
| `src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json` | Enables the flag for local `dotnet run` development (auto-loaded by Blazor WASM when the dev host signals the Development environment). |
| `tests/FamilyHQ.WebUi.Tests/Services/WeatherOverrideServiceTests.cs` | Full unit coverage of the service contract. |
| `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherOverrideDevTool.feature` | Three Reqnroll scenarios verifying the end-to-end override flow. |
| `tests-e2e/FamilyHQ.E2E.Steps/WeatherOverrideSteps.cs` | Step bindings for the feature file. |

### Modified files

| Path | Change |
|------|--------|
| `src/FamilyHQ.WebUi/wwwroot/appsettings.json` | Add `"FeatureWeatherOverride": false` (safe default). |
| `src/FamilyHQ.WebUi/Program.cs` | Register `FeatureFlags` singleton (from configuration) and `IWeatherOverrideService` scoped. |
| `src/FamilyHQ.WebUi/Pages/Settings.razor` | Inject `FeatureFlags`, conditionally render the new tab button and switch case. |
| `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor` | Inject `IWeatherOverrideService`, subscribe to its event, branch in `UpdateOverlay` to use override condition + windy flag when active. |
| `src/FamilyHQ.WebUi/wwwroot/css/app.css` | Append three class rules for the new tab layout. |
| `docker/webui/docker-entrypoint.sh` | Add sed substitution for the flag, parallel to the existing `BACKEND_URL` one. |
| `docker-compose.dev.yml`, `docker-compose.staging.yml`, `docker-compose.preprod.yml`, `docker-compose.prod.yml` | Add `FEATURE_WEATHER_OVERRIDE_ENABLED` to the `webui` service `environment:` block. |
| `.env.dev`, `.env.staging`, `.env.preprod`, `.env.prod`, `.env.example` | Add `FEATURE_WEATHER_OVERRIDE_ENABLED` with appropriate values and a documentation comment. |
| `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs` | Add locators + navigation helper for the new tab. |
| `.agent/docs/architecture.md` | Short paragraph documenting the dev override tab and its feature-flag gate. |

### Untouched

Backend projects, `WeatherStrip.razor`, `WeatherService`, `WeatherRefreshService`, `WeatherController`, user `WeatherSetting` persistence — all confirmed out of scope.

---

## Task 1: Feature flags POCO and WebUi registration

**Files:**
- Create: `src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs`
- Modify: `src/FamilyHQ.WebUi/wwwroot/appsettings.json`
- Create: `src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

No tests in this task — it's pure configuration scaffolding. Task 2 onwards has proper TDD.

- [ ] **Step 1: Create the FeatureFlags POCO**

Create `src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs`:

```csharp
namespace FamilyHQ.WebUi.Configuration;

public class FeatureFlags
{
    public bool WeatherOverrideEnabled { get; set; }
}
```

- [ ] **Step 2: Add the default flag value to appsettings.json**

Current content of `src/FamilyHQ.WebUi/wwwroot/appsettings.json`:

```json
{
    "BackendUrl": "https://localhost:7196"
}
```

Change it to:

```json
{
    "BackendUrl": "https://localhost:7196",
    "FeatureWeatherOverride": false
}
```

The `false` default is the production-safe value. Docker dev/staging containers flip it to `true` via `docker-entrypoint.sh` (Task 7).

- [ ] **Step 3: Create appsettings.Development.json for local dotnet run**

Create `src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json`:

```json
{
    "FeatureWeatherOverride": true
}
```

Blazor WASM automatically loads environment-specific appsettings when the dev server sets the Blazor environment to `Development` (which `dotnet run` does). This file is deliberately minimal — it only overrides the flag, leaving `BackendUrl` to inherit from `appsettings.json`.

- [ ] **Step 4: Register FeatureFlags in Program.cs**

Open `src/FamilyHQ.WebUi/Program.cs`. Find the line (around line 17):

```csharp
var backendUrl = builder.Configuration["BackendUrl"] ?? "https://localhost:5001";
```

Add immediately after it:

```csharp
var featureFlags = new FeatureFlags
{
    WeatherOverrideEnabled = builder.Configuration.GetValue<bool>("FeatureWeatherOverride")
};
builder.Services.AddSingleton(featureFlags);
```

And add this `using` to the top of the file, below the existing `using FamilyHQ.WebUi.Services;` line:

```csharp
using FamilyHQ.WebUi.Configuration;
```

- [ ] **Step 5: Build the WebUi project to confirm the scaffold compiles**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors. Warnings are acceptable if they already existed; no new warnings should appear.

If it fails: read the error. Common issues are a missing `using` statement or a typo in the JSON key. Fix inline and rebuild.

- [ ] **Step 6: Commit**

```bash
git add src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs \
        src/FamilyHQ.WebUi/wwwroot/appsettings.json \
        src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json \
        src/FamilyHQ.WebUi/Program.cs
git commit -m "feat(webui): add FeatureFlags POCO and FeatureWeatherOverride config key"
```

---

## Task 2: WeatherOverrideService (TDD)

**Files:**
- Create: `src/FamilyHQ.WebUi/Services/IWeatherOverrideService.cs`
- Create: `src/FamilyHQ.WebUi/Services/WeatherOverrideService.cs`
- Create: `tests/FamilyHQ.WebUi.Tests/Services/WeatherOverrideServiceTests.cs`
- Modify: `src/FamilyHQ.WebUi/Program.cs`

Strict red-green-refactor. Write the failing tests first.

- [ ] **Step 1: Create the interface**

Create `src/FamilyHQ.WebUi/Services/IWeatherOverrideService.cs`:

```csharp
namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.Enums;

public interface IWeatherOverrideService
{
    bool IsActive { get; }
    WeatherCondition? ActiveCondition { get; }
    bool IsWindy { get; }

    event Action? OnOverrideChanged;

    void Activate(WeatherCondition condition);
    void SelectCondition(WeatherCondition condition);
    void SetWindy(bool isWindy);
    void Deactivate();
}
```

- [ ] **Step 2: Write the failing test file**

Create `tests/FamilyHQ.WebUi.Tests/Services/WeatherOverrideServiceTests.cs`:

```csharp
using FamilyHQ.Core.Enums;
using FamilyHQ.WebUi.Services;
using FluentAssertions;

namespace FamilyHQ.WebUi.Tests.Services;

public class WeatherOverrideServiceTests
{
    [Fact]
    public void Activate_SetsIsActiveAndCondition_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Activate(WeatherCondition.HeavyRain);

        sut.IsActive.Should().BeTrue();
        sut.ActiveCondition.Should().Be(WeatherCondition.HeavyRain);
        fired.Should().Be(1);
    }

    [Fact]
    public void Activate_DefaultsIsWindyToFalse()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        sut.IsWindy.Should().BeFalse();
    }

    [Fact]
    public void Activate_WhenAlreadyInSameState_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Activate(WeatherCondition.Snow); // already active with Snow + windy=false

        fired.Should().Be(0);
        sut.IsActive.Should().BeTrue();
        sut.ActiveCondition.Should().Be(WeatherCondition.Snow);
        sut.IsWindy.Should().BeFalse();
    }

    [Fact]
    public void SelectCondition_WhileActive_ReplacesCondition_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Clear);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Thunder);

        sut.ActiveCondition.Should().Be(WeatherCondition.Thunder);
        fired.Should().Be(1);
    }

    [Fact]
    public void SelectCondition_WhileInactive_IsNoOp_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Fog);

        sut.IsActive.Should().BeFalse();
        sut.ActiveCondition.Should().BeNull();
        fired.Should().Be(0);
    }

    [Fact]
    public void SelectCondition_WithSameCondition_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Cloudy);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SelectCondition(WeatherCondition.Cloudy);

        fired.Should().Be(0);
    }

    [Fact]
    public void SetWindy_WhileActive_UpdatesFlag_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Snow);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        sut.IsWindy.Should().BeTrue();
        fired.Should().Be(1);
    }

    [Fact]
    public void SetWindy_WhileInactive_IsNoOp_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        sut.IsWindy.Should().BeFalse();
        fired.Should().Be(0);
    }

    [Fact]
    public void SetWindy_WithSameValue_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Drizzle);
        sut.SetWindy(true);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.SetWindy(true);

        fired.Should().Be(0);
    }

    [Fact]
    public void Deactivate_ClearsCondition_ResetsIsWindy_FiresEvent()
    {
        var sut = new WeatherOverrideService();
        sut.Activate(WeatherCondition.Sleet);
        sut.SetWindy(true);

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Deactivate();

        sut.IsActive.Should().BeFalse();
        sut.ActiveCondition.Should().BeNull();
        sut.IsWindy.Should().BeFalse();
        fired.Should().Be(1);
    }

    [Fact]
    public void Deactivate_WhileInactive_DoesNotFireEvent()
    {
        var sut = new WeatherOverrideService();

        var fired = 0;
        sut.OnOverrideChanged += () => fired++;

        sut.Deactivate();

        fired.Should().Be(0);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail to compile**

Run:
```bash
dotnet test tests/FamilyHQ.WebUi.Tests/FamilyHQ.WebUi.Tests.csproj --filter FullyQualifiedName~WeatherOverrideServiceTests
```

Expected: **Build failure** with the error `The type or namespace name 'WeatherOverrideService' could not be found`. This is the red phase of TDD — the tests cannot even compile because the implementation does not yet exist.

- [ ] **Step 4: Create the WeatherOverrideService implementation**

Create `src/FamilyHQ.WebUi/Services/WeatherOverrideService.cs`:

```csharp
namespace FamilyHQ.WebUi.Services;

using FamilyHQ.Core.Enums;

public class WeatherOverrideService : IWeatherOverrideService
{
    public bool IsActive { get; private set; }
    public WeatherCondition? ActiveCondition { get; private set; }
    public bool IsWindy { get; private set; }

    public event Action? OnOverrideChanged;

    public void Activate(WeatherCondition condition)
    {
        var changed = !IsActive
                   || ActiveCondition != condition
                   || IsWindy;

        IsActive = true;
        ActiveCondition = condition;
        IsWindy = false;

        if (changed)
            OnOverrideChanged?.Invoke();
    }

    public void SelectCondition(WeatherCondition condition)
    {
        if (!IsActive) return;
        if (ActiveCondition == condition) return;

        ActiveCondition = condition;
        OnOverrideChanged?.Invoke();
    }

    public void SetWindy(bool isWindy)
    {
        if (!IsActive) return;
        if (IsWindy == isWindy) return;

        IsWindy = isWindy;
        OnOverrideChanged?.Invoke();
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        ActiveCondition = null;
        IsWindy = false;
        OnOverrideChanged?.Invoke();
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run:
```bash
dotnet test tests/FamilyHQ.WebUi.Tests/FamilyHQ.WebUi.Tests.csproj --filter FullyQualifiedName~WeatherOverrideServiceTests
```

Expected: **11 tests passing, 0 failing**. If any fail, read the output — the contract in Step 4 is strict about the "no-op means no event" rule, so any failure there is a real bug to fix in the implementation, not the test.

- [ ] **Step 6: Register the service in Program.cs**

Open `src/FamilyHQ.WebUi/Program.cs`. Find the line (around line 63):

```csharp
builder.Services.AddScoped<IDisplaySettingService, DisplaySettingService>();
```

Add immediately after it:

```csharp
builder.Services.AddScoped<IWeatherOverrideService, WeatherOverrideService>();
```

- [ ] **Step 7: Build the full WebUi solution to confirm the registration compiles**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/FamilyHQ.WebUi/Services/IWeatherOverrideService.cs \
        src/FamilyHQ.WebUi/Services/WeatherOverrideService.cs \
        src/FamilyHQ.WebUi/Program.cs \
        tests/FamilyHQ.WebUi.Tests/Services/WeatherOverrideServiceTests.cs
git commit -m "feat(webui): add WeatherOverrideService with transient override state"
```

---

## Task 3: WeatherOverlay integration

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor`

No unit tests — behaviour is covered by the E2E tests in Task 8. This task is small and surgical.

- [ ] **Step 1: Modify WeatherOverlay.razor**

Replace the entire content of `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor` with:

```razor
@namespace FamilyHQ.WebUi.Components.Weather
@using FamilyHQ.WebUi.Services
@implements IAsyncDisposable
@inject IWeatherUiService WeatherService
@inject IWeatherOverrideService OverrideService
@inject IJSRuntime JsRuntime

@code {
    private IJSObjectReference? _module;
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/weather.js");
            WeatherService.OnWeatherChanged += HandleWeatherChanged;
            OverrideService.OnOverrideChanged += HandleOverrideChanged;
            await UpdateOverlay();
        }
    }

    private void HandleWeatherChanged()
    {
        InvokeAsync(UpdateOverlay);
    }

    private void HandleOverrideChanged()
    {
        InvokeAsync(UpdateOverlay);
    }

    private async Task UpdateOverlay()
    {
        if (_module is null || _disposed) return;

        try
        {
            // Override wins when active: force the chosen condition regardless of
            // real weather or the user's weather-enabled setting, so dev/staging can
            // see every animation even when no real weather data exists.
            if (OverrideService.IsActive && OverrideService.ActiveCondition is { } overrideCondition)
            {
                await _module.InvokeVoidAsync(
                    "setWeatherOverlay",
                    overrideCondition.ToString(),
                    OverrideService.IsWindy);
                return;
            }

            if (!WeatherService.IsEnabled || WeatherService.CurrentWeather is null)
            {
                await _module.InvokeVoidAsync("clearWeatherOverlay");
                return;
            }

            var condition = WeatherService.CurrentWeather.Condition.ToString();
            var isWindy = WeatherService.CurrentWeather.IsWindy;
            await _module.InvokeVoidAsync("setWeatherOverlay", condition, isWindy);
        }
        catch (JSDisconnectedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        WeatherService.OnWeatherChanged -= HandleWeatherChanged;
        OverrideService.OnOverrideChanged -= HandleOverrideChanged;
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("clearWeatherOverlay");
            }
            catch (JSDisconnectedException) { }
            await _module.DisposeAsync();
        }
    }
}
```

Diff summary from the existing file:
- Added `@inject IWeatherOverrideService OverrideService`
- Subscribed to `OverrideService.OnOverrideChanged` in `OnAfterRenderAsync`
- Added `HandleOverrideChanged()` method that calls `UpdateOverlay`
- Added the override-wins branch at the top of `UpdateOverlay`
- Unsubscribed from the override event in `DisposeAsync`

- [ ] **Step 2: Build the WebUi project to confirm the overlay compiles**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors.

- [ ] **Step 3: Run the existing WeatherOverrideServiceTests to confirm nothing regressed**

Run:
```bash
dotnet test tests/FamilyHQ.WebUi.Tests/FamilyHQ.WebUi.Tests.csproj --filter FullyQualifiedName~WeatherOverrideServiceTests
```

Expected: **11 tests passing** (unchanged from Task 2).

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor
git commit -m "feat(webui): wire WeatherOverlay to honour override service when active"
```

---

## Task 4: SettingsWeatherOverrideTab component

**Files:**
- Create: `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherOverrideTab.razor`

No unit tests — covered by the E2E tests in Task 8.

- [ ] **Step 1: Create the component file**

Create `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherOverrideTab.razor`:

```razor
@using FamilyHQ.Core.Enums
@using FamilyHQ.WebUi.Services
@implements IDisposable
@inject IWeatherOverrideService OverrideService

<div class="settings-section-content">
    <h2 class="settings-section__title">Weather Override</h2>
    <p class="settings-section__description">
        Dev/staging only. Force the full-screen weather animation to a chosen
        condition for visual testing. Does not affect the weather strip, your
        weather settings, or any data served by the API.
    </p>

    <div class="settings-section">
        <div class="weather-override__toggle-row">
            <span class="settings-label">Override active</span>
            <button type="button"
                    class="@($"pill-toggle {(OverrideService.IsActive ? "pill-toggle--on" : "pill-toggle--off")}")"
                    data-testid="weather-override-toggle"
                    @onclick="ToggleOverride">
                @(OverrideService.IsActive ? "On" : "Off")
            </button>
        </div>

        @if (OverrideService.IsActive)
        {
            <p class="settings-hint mt-3">Tap a condition to engage its animation immediately.</p>

            <div class="weather-override__modifier-row mt-2">
                <span class="settings-label">Windy</span>
                <button type="button"
                        class="@($"pill-toggle {(OverrideService.IsWindy ? "pill-toggle--on" : "pill-toggle--off")}")"
                        data-testid="weather-override-windy"
                        @onclick="ToggleWindy">
                    @(OverrideService.IsWindy ? "On" : "Off")
                </button>
            </div>

            <div class="weather-override__condition-grid mt-2">
                @foreach (var condition in AllConditions)
                {
                    var isSelected = OverrideService.ActiveCondition == condition;
                    <button type="button"
                            class="@($"pill-toggle {(isSelected ? "pill-toggle--on" : "pill-toggle--off")}")"
                            data-testid="@($"weather-override-condition-{condition}")"
                            @onclick="() => SelectCondition(condition)">
                        @FormatCondition(condition)
                    </button>
                }
            </div>
        }
    </div>
</div>

@code {
    private static readonly WeatherCondition[] AllConditions = Enum.GetValues<WeatherCondition>();

    protected override void OnInitialized()
    {
        OverrideService.OnOverrideChanged += HandleChanged;
    }

    private void HandleChanged() => InvokeAsync(StateHasChanged);

    private void ToggleOverride()
    {
        if (OverrideService.IsActive)
        {
            OverrideService.Deactivate();
        }
        else
        {
            // Default to Clear when first enabling so the user sees something change.
            OverrideService.Activate(WeatherCondition.Clear);
        }
    }

    private void ToggleWindy() => OverrideService.SetWindy(!OverrideService.IsWindy);

    private void SelectCondition(WeatherCondition condition) => OverrideService.SelectCondition(condition);

    private static string FormatCondition(WeatherCondition condition) => condition switch
    {
        WeatherCondition.PartlyCloudy => "Partly Cloudy",
        WeatherCondition.LightRain => "Light Rain",
        WeatherCondition.HeavyRain => "Heavy Rain",
        _ => condition.ToString()
    };

    public void Dispose()
    {
        OverrideService.OnOverrideChanged -= HandleChanged;
    }
}
```

- [ ] **Step 2: Build to confirm the component compiles**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors. The new Razor file must compile; if it fails check the `@using` imports and the interpolated attribute syntax.

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherOverrideTab.razor
git commit -m "feat(webui): add SettingsWeatherOverrideTab with pill-based controls"
```

---

## Task 5: Settings.razor tab gating

**Files:**
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor`

- [ ] **Step 1: Add FeatureFlags injection and gate the new tab**

Open `src/FamilyHQ.WebUi/Pages/Settings.razor`. Current top of file:

```razor
@page "/settings"
@using FamilyHQ.WebUi.Services.Auth
@using FamilyHQ.WebUi.Components.Settings
@inject IAuthenticationService AuthService
@inject NavigationManager Navigation
```

Add two lines after the existing `@using` and `@inject` directives:

```razor
@page "/settings"
@using FamilyHQ.WebUi.Services.Auth
@using FamilyHQ.WebUi.Components.Settings
@using FamilyHQ.WebUi.Configuration
@inject IAuthenticationService AuthService
@inject NavigationManager Navigation
@inject FeatureFlags FeatureFlags
```

- [ ] **Step 2: Add the conditional tab button**

Find the existing Display tab button:

```razor
        <button class="settings-tab @(_activeTab == "display"  ? "settings-tab--active" : "")"
                data-testid="tab-display"
                @onclick='() => SetTab("display")'>
            <span class="settings-tab__icon" aria-hidden="true">🖥</span>
            <span class="settings-tab__label">Display</span>
        </button>
    </nav>
```

Insert the new Weather Override tab button directly after the Display button, before the closing `</nav>` tag. Only render it when the feature flag is on:

```razor
        <button class="settings-tab @(_activeTab == "display"  ? "settings-tab--active" : "")"
                data-testid="tab-display"
                @onclick='() => SetTab("display")'>
            <span class="settings-tab__icon" aria-hidden="true">🖥</span>
            <span class="settings-tab__label">Display</span>
        </button>
        @if (FeatureFlags.WeatherOverrideEnabled)
        {
            <button class="settings-tab @(_activeTab == "weather-override" ? "settings-tab--active" : "")"
                    data-testid="tab-weather-override"
                    @onclick='() => SetTab("weather-override")'>
                <span class="settings-tab__icon" aria-hidden="true">🧪</span>
                <span class="settings-tab__label">Weather Override</span>
            </button>
        }
    </nav>
```

- [ ] **Step 3: Add the conditional switch case**

Find the existing switch block:

```razor
        @switch (_activeTab)
        {
            case "general":
                <SettingsGeneralTab />
                break;
            case "location":
                <SettingsLocationTab />
                break;
            case "weather":
                <SettingsWeatherTab />
                break;
            case "display":
                <SettingsDisplayTab />
                break;
            case "calendars":
                <SettingsCalendarsTab />
                break;
        }
```

Add a new case **with a `when` guard** for defensive safety (so the tab stays inert even if `_activeTab` is somehow set to `"weather-override"` in a build where the flag is off):

```razor
        @switch (_activeTab)
        {
            case "general":
                <SettingsGeneralTab />
                break;
            case "location":
                <SettingsLocationTab />
                break;
            case "weather":
                <SettingsWeatherTab />
                break;
            case "display":
                <SettingsDisplayTab />
                break;
            case "calendars":
                <SettingsCalendarsTab />
                break;
            case "weather-override" when FeatureFlags.WeatherOverrideEnabled:
                <SettingsWeatherOverrideTab />
                break;
        }
```

- [ ] **Step 4: Build to confirm Settings.razor compiles**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Pages/Settings.razor
git commit -m "feat(webui): gate Weather Override settings tab behind FeatureFlags"
```

---

## Task 6: CSS styles for the override tab

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

- [ ] **Step 1: Append the three new class rules**

Open `src/FamilyHQ.WebUi/wwwroot/css/app.css`. Find the existing `.pill-toggle--shared.pill-toggle--on` rule near line 1290. Append the following three rules **directly after** it (they logically belong with the other pill-related rules):

```css

.weather-override__toggle-row,
.weather-override__modifier-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    margin-top: 16px;
}

.weather-override__condition-grid {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    margin-top: 12px;
}
```

No CSS variables are used — the theme system in `app.css` uses literal pixel values for spacing and only variables for colours (`--theme-accent`, `--theme-text-muted`). Following the existing convention.

- [ ] **Step 2: Build to confirm CSS didn't break the build**

Run:
```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: **Build succeeded** with 0 errors. (CSS doesn't go through the compiler but a full publish step includes it, so a broken CSS file could fail a later step.)

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(webui): add layout styles for Weather Override tab rows and grid"
```

---

## Task 7: Docker env var wiring

**Files:**
- Modify: `docker/webui/docker-entrypoint.sh`
- Modify: `docker-compose.dev.yml`, `docker-compose.staging.yml`, `docker-compose.preprod.yml`, `docker-compose.prod.yml`
- Modify: `.env.dev`, `.env.staging`, `.env.preprod`, `.env.prod`, `.env.example`

This task has no unit tests; verification is via the manual smoke check in Task 10.

- [ ] **Step 1: Extend docker-entrypoint.sh with the sed substitution**

Current content of `docker/webui/docker-entrypoint.sh`:

```sh
#!/bin/sh
set -e

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"
HEALTH_FILE="/usr/share/nginx/html/health"

if [ -n "$BACKEND_URL" ]; then
    sed -i "s|https://localhost:7196|${BACKEND_URL}|g" "$CONFIG_FILE"
fi

printf '{"status":"healthy","service":"webui","startedAt":"%s"}' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HEALTH_FILE"
chmod 644 "$HEALTH_FILE"

exec nginx -g "daemon off;"
```

Add a new sed block after the `BACKEND_URL` one:

```sh
#!/bin/sh
set -e

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"
HEALTH_FILE="/usr/share/nginx/html/health"

if [ -n "$BACKEND_URL" ]; then
    sed -i "s|https://localhost:7196|${BACKEND_URL}|g" "$CONFIG_FILE"
fi

# Dev/staging only: flip the weather override feature flag on.
# Only an explicit "true" flips it; any other value leaves the default (false),
# so preprod and production are safe even if the env var is missing.
if [ "$FEATURE_WEATHER_OVERRIDE_ENABLED" = "true" ]; then
    sed -i 's|"FeatureWeatherOverride": false|"FeatureWeatherOverride": true|' "$CONFIG_FILE"
fi

printf '{"status":"healthy","service":"webui","startedAt":"%s"}' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HEALTH_FILE"
chmod 644 "$HEALTH_FILE"

exec nginx -g "daemon off;"
```

- [ ] **Step 2: Add the env var to `docker-compose.dev.yml`**

Open `docker-compose.dev.yml`. The current `webui` service has:

```yaml
  webui:
    container_name: familyhq-webui-dev
    env_file: .env
    environment:
      - BACKEND_URL=${BACKEND_URL}
```

Add the new env var to the `environment:` block:

```yaml
  webui:
    container_name: familyhq-webui-dev
    env_file: .env
    environment:
      - BACKEND_URL=${BACKEND_URL}
      - FEATURE_WEATHER_OVERRIDE_ENABLED=${FEATURE_WEATHER_OVERRIDE_ENABLED}
```

- [ ] **Step 3: Apply the same change to staging, preprod, and prod compose files**

Open each of:
- `docker-compose.staging.yml`
- `docker-compose.preprod.yml`
- `docker-compose.prod.yml`

And add the identical line `- FEATURE_WEATHER_OVERRIDE_ENABLED=${FEATURE_WEATHER_OVERRIDE_ENABLED}` to the `webui` service's `environment:` block, immediately after the existing `BACKEND_URL` line. The docker-compose structure is the same in all four files.

- [ ] **Step 4: Add the flag to the five env files**

Open `.env.dev` and append:

```
# WebUi — Weather override dev tool (dev/staging only; leave false in preprod/prod)
FEATURE_WEATHER_OVERRIDE_ENABLED=true
```

Open `.env.staging` and append the same two lines (same `true` value).

Open `.env.preprod` and append:

```
# WebUi — Weather override dev tool (dev/staging only; leave false in preprod/prod)
FEATURE_WEATHER_OVERRIDE_ENABLED=false
```

Open `.env.prod` and append the same two lines (same `false` value).

Open `.env.example` and append:

```
# WebUi — Weather override dev tool
# Set to "true" in dev/staging to show the Weather Override settings tab
# used to preview each WeatherCondition animation. MUST be "false" (or absent)
# in preprod and production. Any value other than the literal string "true" is
# treated as false.
FEATURE_WEATHER_OVERRIDE_ENABLED=false
```

- [ ] **Step 5: Commit**

```bash
git add docker/webui/docker-entrypoint.sh \
        docker-compose.dev.yml docker-compose.staging.yml \
        docker-compose.preprod.yml docker-compose.prod.yml \
        .env.dev .env.staging .env.preprod .env.prod .env.example
git commit -m "feat(deploy): wire FEATURE_WEATHER_OVERRIDE_ENABLED env var to webui container"
```

---

## Task 8: E2E feature file, page-object updates, and step bindings

**Files:**
- Modify: `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`
- Create: `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherOverrideDevTool.feature`
- Create: `tests-e2e/FamilyHQ.E2E.Steps/WeatherOverrideSteps.cs`

- [ ] **Step 1: Extend SettingsPage with locators for the new tab**

Open `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`. Find the existing tab navigation block (around line 20-24):

```csharp
    // Tab navigation
    public ILocator GeneralTab  => Page.GetByTestId("tab-general");
    public ILocator LocationTab => Page.GetByTestId("tab-location");
    public ILocator WeatherTab  => Page.GetByTestId("tab-weather");
    public ILocator DisplayTab  => Page.GetByTestId("tab-display");
```

Add one more line for the override tab, keeping alignment:

```csharp
    // Tab navigation
    public ILocator GeneralTab         => Page.GetByTestId("tab-general");
    public ILocator LocationTab        => Page.GetByTestId("tab-location");
    public ILocator WeatherTab         => Page.GetByTestId("tab-weather");
    public ILocator DisplayTab         => Page.GetByTestId("tab-display");
    public ILocator WeatherOverrideTab => Page.GetByTestId("tab-weather-override");
```

Then find the existing `NavigateToWeatherTabAsync` method (around line 77) and add a new helper after it — before the `NavigateToCalendarsTabAsync` method:

```csharp
    public async Task NavigateToWeatherOverrideTabAsync()
    {
        await NavigateAndWaitAsync();
        await WeatherOverrideTab.ClickAsync();
        await Page.GetByTestId("weather-override-toggle").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }
```

Also add three new locator helpers in the "Weather tab" locator block, keeping them grouped with the override-specific controls rather than mingling with the real weather settings:

Find the existing weather tab locator block (around line 37-43):

```csharp
    // Weather tab (for WeatherSteps access)
    public ILocator WeatherEnabledToggle  => Page.Locator("#weather-enabled-toggle");
    public ILocator TemperatureUnitSelect => Page.Locator("#temperature-unit");
    public ILocator PollIntervalInput     => Page.Locator("#poll-interval");
    public ILocator WindThresholdInput    => Page.Locator("#wind-threshold");
    public ILocator WeatherSaveBtn        => Page.Locator(".settings-btn").First;
    public ILocator WeatherCancelBtn      => Page.Locator(".settings-btn--ghost");
    public ILocator WeatherSuccessMessage => Page.Locator(".settings-hint").Filter(new() { HasText = "Settings saved." });
```

Add the new block immediately after it:

```csharp
    // Weather Override tab (dev/staging only)
    public ILocator WeatherOverrideToggle    => Page.GetByTestId("weather-override-toggle");
    public ILocator WeatherOverrideWindyPill => Page.GetByTestId("weather-override-windy");
    public ILocator WeatherOverrideConditionPill(string condition) =>
        Page.GetByTestId($"weather-override-condition-{condition}");
```

- [ ] **Step 2: Create the step bindings class**

Create `tests-e2e/FamilyHQ.E2E.Steps/WeatherOverrideSteps.cs`:

```csharp
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WeatherOverrideSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SettingsPage _settingsPage;
    private readonly DashboardPage _dashboardPage;

    public WeatherOverrideSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(page);
        _dashboardPage = new DashboardPage(page);
    }

    [When(@"I open the ""Weather Override"" tab")]
    public async Task WhenIOpenTheWeatherOverrideTab()
    {
        await _settingsPage.NavigateToWeatherOverrideTabAsync();
    }

    [When(@"I toggle override on")]
    public async Task WhenIToggleOverrideOn()
    {
        var toggle = _settingsPage.WeatherOverrideToggle;
        var classes = await toggle.GetAttributeAsync("class") ?? string.Empty;
        if (!classes.Contains("pill-toggle--on"))
        {
            await toggle.ClickAsync();
            await Assertions.Expect(toggle).ToHaveClassAsync(
                new System.Text.RegularExpressions.Regex("pill-toggle--on"),
                new() { Timeout = 5000 });
        }
    }

    [When(@"I toggle override off")]
    public async Task WhenIToggleOverrideOff()
    {
        var toggle = _settingsPage.WeatherOverrideToggle;
        var classes = await toggle.GetAttributeAsync("class") ?? string.Empty;
        if (classes.Contains("pill-toggle--on"))
        {
            await toggle.ClickAsync();
            await Assertions.Expect(toggle).ToHaveClassAsync(
                new System.Text.RegularExpressions.Regex("pill-toggle--off"),
                new() { Timeout = 5000 });
        }
    }

    [When(@"I select the ""(.*)"" condition")]
    public async Task WhenISelectTheCondition(string conditionName)
    {
        var pill = _settingsPage.WeatherOverrideConditionPill(conditionName);
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("pill-toggle--on"),
            new() { Timeout = 5000 });
    }

    [When(@"I toggle Windy on")]
    public async Task WhenIToggleWindyOn()
    {
        var pill = _settingsPage.WeatherOverrideWindyPill;
        var classes = await pill.GetAttributeAsync("class") ?? string.Empty;
        if (!classes.Contains("pill-toggle--on"))
        {
            await pill.ClickAsync();
            await Assertions.Expect(pill).ToHaveClassAsync(
                new System.Text.RegularExpressions.Regex("pill-toggle--on"),
                new() { Timeout = 5000 });
        }
    }

    [Then(@"the weather overlay element has the class ""(.*)""")]
    public async Task ThenTheWeatherOverlayElementHasTheClass(string className)
    {
        await Assertions.Expect(_dashboardPage.WeatherOverlay).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(className),
            new() { Timeout = 10000 });
    }

    [Then(@"the weather overlay element does not have the class ""(.*)""")]
    public async Task ThenTheWeatherOverlayElementDoesNotHaveTheClass(string className)
    {
        // Poll until the class disappears or timeout fires.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var classes = await _dashboardPage.WeatherOverlay.GetAttributeAsync("class") ?? string.Empty;
            if (!classes.Contains(className))
                return;
            await Task.Delay(100);
        }
        var finalClasses = await _dashboardPage.WeatherOverlay.GetAttributeAsync("class") ?? string.Empty;
        finalClasses.Should().NotContain(className,
            $"overlay should have rolled back after deactivating override, but still carries {className}");
    }
}
```

Note: the two `Then` steps in this file use slightly different phrasing ("the weather overlay element has the class") from the existing `WeatherSteps.cs` step ("the weather overlay has class X") to avoid step-binding collisions. The existing `WeatherSteps` bindings for "the weather overlay has class" stay untouched.

- [ ] **Step 3: Create the feature file**

Create `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherOverrideDevTool.feature`:

```gherkin
Feature: Weather override dev tool
  As a developer working in dev or staging
  I want to manually force weather animations
  So that I can visually verify each condition's animation without seeding real weather data

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"

  Scenario: Activating override and selecting a condition engages the matching animation
    When I navigate to the Settings page
    And I open the "Weather Override" tab
    And I toggle override on
    And I select the "HeavyRain" condition
    Then the weather overlay element has the class "weather-heavyrain"

  Scenario: Toggling Windy adds the windy class
    When I navigate to the Settings page
    And I open the "Weather Override" tab
    And I toggle override on
    And I select the "Snow" condition
    And I toggle Windy on
    Then the weather overlay element has the class "weather-snow"
    And the weather overlay element has the class "weather-windy"

  Scenario: Deactivating override returns the overlay to real weather behaviour
    Given weather is enabled
    And the user has a saved location "TestCity" at 55.95, -3.19
    And weather data is seeded for the location:
      | Current Temp | Current Code | Wind Speed |
      | 12           | 0            | 5          |
    When I wait for weather data to load
    And I navigate to the Settings page
    And I open the "Weather Override" tab
    And I toggle override on
    And I select the "HeavyRain" condition
    Then the weather overlay element has the class "weather-heavyrain"
    When I toggle override off
    Then the weather overlay element does not have the class "weather-heavyrain"
```

The third scenario depends on existing `WeatherSteps` bindings (`weather is enabled`, `the user has a saved location...`, `weather data is seeded for the location:`, `I wait for weather data to load`) — those are already defined in `WeatherSteps.cs` and reused here verbatim. Scenarios one and two exercise the override in isolation without seeding any real weather, verifying the spec's requirement that the override works even when no real weather data exists.

The step `I navigate to the Settings page` already exists in `SettingsSteps.cs`; verify its exact phrasing matches when implementing by grepping the existing settings feature files:

```bash
grep -r "navigate to the Settings page" tests-e2e/FamilyHQ.E2E.Features/
```

If the phrasing differs (e.g. `I go to the settings page`), use whichever the existing `SettingsSteps.cs` binds. Do not add a duplicate step binding.

- [ ] **Step 4: Build the E2E project to confirm the new step class compiles**

Run:
```bash
dotnet build tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj
```

Expected: **Build succeeded** with 0 errors. If there are step-binding ambiguity errors, rename the colliding step in `WeatherOverrideSteps.cs` to be more specific (e.g. prefix with "override").

- [ ] **Step 5: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs \
        tests-e2e/FamilyHQ.E2E.Steps/WeatherOverrideSteps.cs \
        tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherOverrideDevTool.feature
git commit -m "test(e2e): add weather override dev tool acceptance scenarios"
```

---

## Task 9: Architecture doc update

**Files:**
- Modify: `.agent/docs/architecture.md`

- [ ] **Step 1: Read the current architecture doc to find the right place for the addition**

Run:
```bash
grep -n "Weather\|Feature flag\|Settings" .agent/docs/architecture.md
```

Look for the section that documents the Settings page, the WebUi structure, or feature flags. If none of these sections exist, add a new `## Feature flags` section at the end of the file.

- [ ] **Step 2: Add a paragraph describing the override tab and its gating**

Append (or insert into the appropriate existing section) the following paragraph. Adjust the heading level to match the surrounding document if you are inserting rather than appending:

```markdown
### Weather Override (dev/staging only)

The Settings page has a fifth tab, **Weather Override**, rendered only when
`FeatureFlags.WeatherOverrideEnabled` is true. The flag is sourced from the
WebUi's `appsettings.json` key `FeatureWeatherOverride`, which is injected
into the published bundle at container startup by `docker/webui/docker-entrypoint.sh`
based on the `FEATURE_WEATHER_OVERRIDE_ENABLED` environment variable. Dev and
staging set this to `true`; preprod and production set it to `false`. Local
`dotnet run` inherits `true` from `wwwroot/appsettings.Development.json`.

When the tab's "Override active" pill is on, a developer can tap any
`WeatherCondition` and optionally toggle the Windy modifier to immediately
force the full-screen weather animation (`WeatherOverlay`) to that condition.
The override is purely client-side transient state held in a scoped
`IWeatherOverrideService` and is never persisted — refreshing the browser
reverts to the real weather pipeline. The `WeatherStrip`, backend API, user
`WeatherSetting`, and real weather data flow are untouched.
```

- [ ] **Step 3: Commit**

```bash
git add .agent/docs/architecture.md
git commit -m "docs: document Weather Override dev tool in architecture.md"
```

---

## Task 10: Local build and unit-test verification

**Files:** None (verification only).

- [ ] **Step 1: Build the entire solution**

Run:
```bash
dotnet build
```

Expected: **Build succeeded** with 0 errors across all projects. Warnings are acceptable only if they existed on `dev` before this branch; no new warnings should appear.

- [ ] **Step 2: Run all WebUi unit tests**

Run:
```bash
dotnet test tests/FamilyHQ.WebUi.Tests/FamilyHQ.WebUi.Tests.csproj
```

Expected: **All tests passing**, including the 11 new `WeatherOverrideServiceTests`. If any previously passing test now fails, investigate — the new code should not have touched anything unrelated to the override feature, so a regression here points to an accidental change somewhere in Task 1–9.

- [ ] **Step 3: Local smoke via `dotnet run` (manual — requires user)**

Explain to the user that this step needs a manual browser check before continuing:

> "I'm ready to do a local smoke check. Please run `dotnet run --project src/FamilyHQ.WebUi` in a separate terminal, open the app in a browser, sign in, navigate to **Settings**, and confirm the **Weather Override** tab is present. Tap it, flip the toggle on, tap **HeavyRain**, and confirm the full-screen animation changes. Let me know when done."

Wait for the user's confirmation before marking this step complete.

---

## Task 11: Docker image sed-substitution smoke (requires approval)

**Files:** None (verification only).

**⚠️ This task performs a full WebUi Docker image build and runs it in a container. Per `AGENTS.md`, "Running full project build" and container runs require user approval. Pause and get explicit approval before running Step 1.**

- [ ] **Step 1: Get user approval for the Docker build**

Ask:

> "Ready for the Docker-image sed smoke test. This will run `docker build` on the WebUi Dockerfile, then start the container twice (once with `FEATURE_WEATHER_OVERRIDE_ENABLED=true`, once with it unset) to verify the entrypoint script flips the flag correctly. Approve?"

Wait for explicit approval.

- [ ] **Step 2: Build the WebUi image**

Run:
```bash
docker build -f docker/webui/Dockerfile -t familyhq-webui:override-test .
```

Expected: **Image built successfully**.

- [ ] **Step 3: Run the image with the flag on and inspect appsettings.json**

Run:
```bash
docker run --rm \
    -e FEATURE_WEATHER_OVERRIDE_ENABLED=true \
    -e BACKEND_URL=http://test \
    --entrypoint /bin/sh \
    familyhq-webui:override-test \
    -c "/docker-entrypoint.sh & sleep 1; cat /usr/share/nginx/html/appsettings.json"
```

Expected output contains `"FeatureWeatherOverride": true`.

- [ ] **Step 4: Run the image with the flag off and inspect appsettings.json**

Run:
```bash
docker run --rm \
    -e BACKEND_URL=http://test \
    --entrypoint /bin/sh \
    familyhq-webui:override-test \
    -c "/docker-entrypoint.sh & sleep 1; cat /usr/share/nginx/html/appsettings.json"
```

Expected output contains `"FeatureWeatherOverride": false` (the default was not flipped because the env var was absent).

- [ ] **Step 5: Clean up the test image**

Run:
```bash
docker rmi familyhq-webui:override-test
```

---

## Task 12: E2E run against dev environment (requires approval)

**Files:** None (verification only).

**⚠️ Running the E2E suite requires user approval per `AGENTS.md`. Pause and get explicit approval before running Step 1.**

- [ ] **Step 1: Get user approval for the E2E run**

Ask:

> "Ready to run the new `WeatherOverrideDevTool.feature` scenarios against the dev environment. Approve?"

Wait for explicit approval.

- [ ] **Step 2: Run only the new scenarios**

Run:
```bash
dotnet test tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj \
    --filter "FullyQualifiedName~WeatherOverrideDevTool"
```

Expected: **3 scenarios passing**. If any fail, read the Playwright output and screenshots before deciding whether the fault is in the feature code, the step bindings, or the Page Object. Do not use `--no-verify` or other bypass flags to "make it green". Fix the root cause.

- [ ] **Step 3: Run the full weather-related E2E suite to verify no regression**

Run:
```bash
dotnet test tests-e2e/FamilyHQ.E2E.Features/FamilyHQ.E2E.Features.csproj \
    --filter "FullyQualifiedName~Weather"
```

Expected: All existing weather scenarios in `Weather.feature` and `WeatherSettings.feature` still pass alongside the new `WeatherOverrideDevTool.feature`.

---

## Task 13: CI gate, push, and PR

Use the `ci-gate` skill to push the branch and verify CI before raising a PR.

- [ ] **Step 1: Invoke the ci-gate skill**

Per `AGENTS.md`, before raising a PR: "Read `.agent/skills/ci-gate/SKILL.md`". Invoke that skill, which walks through pushing the branch and polling Jenkins for a successful build.

- [ ] **Step 2: After CI is green, open a PR**

Use `gh pr create` with a title under 70 characters. Suggested:

> `feat(webui): dev/staging weather override tool for animation testing`

Body should summarise the three-layer change: env-var-gated feature flag, client-side override service, and UI + overlay integration. Include a reference to the spec and plan.

- [ ] **Step 3: Confirm PR URL is returned to the user**

Paste the PR URL back to the user when `gh pr create` returns it.

---

## Notes and Principles

- **DRY / YAGNI:** No `IFeatureFlagsService` layer for a single flag. No persistence for a transient dev tool. No unit for a component that only delegates.
- **TDD:** Strict red-green-refactor only where the implementation is logic-heavy (the service). Config, thin-shell components, and docker plumbing are covered by integration/E2E tests instead.
- **Frequent commits:** Nine logical commits across Tasks 1–9, each independently reviewable.
- **Reversibility:** Every change is additive except for the overlay file rewrite in Task 3, which has a clean diff (one inject, one event subscribe, one branch, one unsubscribe). Reverting the branch unambiguously restores the previous behaviour.
- **User-approval gates:** Tasks 11 and 12 (Docker build and full E2E run) explicitly pause for approval. Task 10 step 3 pauses for a manual local smoke check.
