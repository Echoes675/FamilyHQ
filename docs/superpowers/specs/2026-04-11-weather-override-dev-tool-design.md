# Weather Override Dev Tool — Design

**Date:** 2026-04-11
**Branch:** `feature/weather-override-dev-tool`
**Status:** Draft — awaiting user review

## Summary

Add a dev/staging-only UI tool that lets a developer manually force the full-screen weather animation (`WeatherOverlay`) to any `WeatherCondition` value, optionally with the "windy" modifier, so every animation can be visually verified without seeding or waiting on real weather data. The tool lives on a new Settings tab that is gated by a feature flag sourced from an environment variable. Preprod and production never see the tab.

The override is purely client-side and purely additive: it does not touch the backend, the weather API, the `WeatherSetting` database row, the `WeatherStrip` header component, or any user-visible weather data. It only short-circuits one call inside `WeatherOverlay.UpdateOverlay()`.

## Goals

- A developer in dev or staging can open Settings, switch to a new "Weather Override" tab, flip a pill toggle on, and tap any of the ten `WeatherCondition` values to see the matching animation engage immediately.
- An independent "Windy" pill toggles the windy modifier on the active condition.
- Flipping the override off returns the overlay to normal real-weather behaviour on the next `UpdateOverlay` call.
- In preprod and production, the tab is absent and no override code path is reachable from the UI.
- The feature flag is controlled by an environment variable in the existing `.env.{environment}` → `docker-compose.{environment}.yml` → container-env-var → `docker-entrypoint.sh` pipeline that already ships `BACKEND_URL` into the WASM client.
- No changes to `WeatherController`, `WeatherService`, `WeatherRefreshService`, the OpenMeteo provider, or the `WeatherSetting` database row.

## Non-goals

- Persisting the override across page reloads. The override is transient session state — refreshing the browser returns to real weather.
- Offering temperature or wind-speed overrides. Only condition and the boolean windy modifier are in scope. The strip keeps showing real temperature and real wind from the provider.
- Overriding the header `WeatherStrip` icon, temperature, or condition label.
- Hiding the strip while override is active.
- Adding a dedicated preprod/prod E2E suite. A future feature will scope that work; this design deliberately avoids pre-committing to an approach.
- Extending `WeatherCondition` or adding new animation classes. We drive the existing `weather.js` module with existing enum values only.

## Configuration flow — env var → WASM client

We mirror the existing `BACKEND_URL` plumbing exactly. No new backend endpoint and no API changes. The entire chain is:

1. **`.env.{environment}` files** — new variable `FEATURE_WEATHER_OVERRIDE_ENABLED`.
   - `.env.dev` → `true`
   - `.env.staging` → `true`
   - `.env.preprod` → `false`
   - `.env.prod` → `false`
   - `.env.example` → documented with a comment noting it's a dev/staging UI aid only.

2. **`docker-compose.{environment}.yml`** — each file adds one line to the `webui` service's `environment:` block:
   ```yaml
   - FEATURE_WEATHER_OVERRIDE_ENABLED=${FEATURE_WEATHER_OVERRIDE_ENABLED}
   ```

3. **Container entrypoint** (`docker/webui/docker-entrypoint.sh`) adds a sed substitution parallel to the existing `BACKEND_URL` one:
   ```sh
   if [ "$FEATURE_WEATHER_OVERRIDE_ENABLED" = "true" ]; then
       sed -i 's|"FeatureWeatherOverride": false|"FeatureWeatherOverride": true|' "$CONFIG_FILE"
   fi
   ```
   The condition checks for the literal string `"true"`. Any other value (empty, `false`, typos) leaves the default in place, so preprod and production are safe even if the env var is missing or misconfigured.

4. **Default in `src/FamilyHQ.WebUi/wwwroot/appsettings.json`** — one new key added to the existing file:
   ```json
   "FeatureWeatherOverride": false
   ```
   This is the production-safe default. A broken sed run or a missing env var both land on `false`.

5. **Local `dotnet run`** — new file `src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json`:
   ```json
   { "FeatureWeatherOverride": true }
   ```
   Blazor WASM auto-loads environment-specific appsettings when the dev server sets the Blazor environment header to `Development`, so a developer running locally sees the tab without editing committed config.

6. **`Program.cs`** reads the flag once and registers it as a singleton POCO:
   ```csharp
   var featureFlags = new FeatureFlags
   {
       WeatherOverrideEnabled = builder.Configuration.GetValue<bool>("FeatureWeatherOverride")
   };
   builder.Services.AddSingleton(featureFlags);
   ```
   `FeatureFlags` is a tiny POCO in `src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs`. Only one flag exists today; a full `IFeatureFlagsService` layer would be premature.

## Client-side override state service

A single scoped service holds transient override state and notifies subscribers when it changes. No persistence: override resets on page reload.

**`IWeatherOverrideService`** — new, in `src/FamilyHQ.WebUi/Services/`:

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

### Semantics

The event-firing contract is uniform across all four methods: **`OnOverrideChanged` fires if and only if the call produced a real change in any of `IsActive`, `ActiveCondition`, or `IsWindy`.** This avoids spurious overlay repaints.

- `Activate(condition)` transitions state to `(IsActive=true, ActiveCondition=condition, IsWindy=false)`. Fires if that's a change; no-op otherwise.
- `SelectCondition(condition)` replaces `ActiveCondition`. No-op if `IsActive == false` or if the new condition equals the current one.
- `SetWindy(isWindy)` updates `IsWindy`. No-op if `IsActive == false` or if the value is unchanged.
- `Deactivate()` transitions state to `(IsActive=false, ActiveCondition=null, IsWindy=false)`. No-op if already inactive.

### Implementation

`WeatherOverrideService` is a plain class with three private fields, matching the interface exactly. `OnOverrideChanged?.Invoke()` is called only when state actually changes.

### Registration

```csharp
builder.Services.AddScoped<IWeatherOverrideService, WeatherOverrideService>();
```

Scoped matches how `IWeatherUiService` is registered and the rest of the WASM DI conventions.

### Why a service rather than component state

`WeatherOverlay` lives in `MainLayout`, not on the Settings page. The Settings tab needs to share state with the overlay across navigations: the user can tap "HeavyRain", then navigate back to the dashboard, and the overlay should still show heavy rain until they come back and flip it off. A scoped service is the idiomatic Blazor way to share state across components.

## New Settings tab — `SettingsWeatherOverrideTab`

A new tab component that is only rendered when the feature flag is on, using the existing `pill-toggle` styling for touchscreen consistency with the calendar settings page.

### `Settings.razor` changes

Inject the feature flags POCO and gate both the tab button and the switch case:

```razor
@inject FamilyHQ.WebUi.Configuration.FeatureFlags FeatureFlags

...

@if (FeatureFlags.WeatherOverrideEnabled)
{
    <button class="settings-tab @(_activeTab == "weather-override" ? "settings-tab--active" : "")"
            data-testid="tab-weather-override"
            @onclick='() => SetTab("weather-override")'>
        <span class="settings-tab__icon" aria-hidden="true">🧪</span>
        <span class="settings-tab__label">Weather Override</span>
    </button>
}

...

case "weather-override" when FeatureFlags.WeatherOverrideEnabled:
    <SettingsWeatherOverrideTab />
    break;
```

The `when` guard on the switch case is defensive: even if someone sets `_activeTab = "weather-override"` programmatically in a build where the flag is false, the tab content stays inert.

### `SettingsWeatherOverrideTab.razor`

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

### Styling

Two new rules added alongside the existing `pill-toggle` styles in `src/FamilyHQ.WebUi/wwwroot/css/app.css`, using only existing theme variables so `ui-theming` rules stay untouched:

```css
.weather-override__toggle-row,
.weather-override__modifier-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--spacing-md);
}

.weather-override__condition-grid {
    display: flex;
    flex-wrap: wrap;
    gap: var(--spacing-sm);
}
```

The exact variable names will be confirmed against the current `app.css` during implementation. The `ui-theming` skill will be consulted at implementation time.

### Behaviour notes

- Tapping the **Override active** pill while off → `Activate(Clear)`. The condition grid and Windy pill appear; `Clear` is highlighted; `Windy` is off.
- Tapping **Override active** while on → `Deactivate()`. Grid and Windy pill hide. Overlay reverts to real weather on the next update.
- Tapping a condition pill while active → `SelectCondition(...)`. Overlay re-animates to the new condition.
- Tapping the Windy pill while active → `SetWindy(!IsWindy)`. Overlay adds or removes the `weather-windy` class immediately.
- Deactivating resets Windy back to false. Re-activating starts fresh on `Clear` and `Windy = off`.

## WeatherOverlay integration

`WeatherOverlay` is the only component whose render behaviour the override affects. We add one injected dependency, one event subscription, and one branch at the top of `UpdateOverlay()`.

### Modified: `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor`

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

    private void HandleWeatherChanged() => InvokeAsync(UpdateOverlay);
    private void HandleOverrideChanged() => InvokeAsync(UpdateOverlay);

    private async Task UpdateOverlay()
    {
        if (_module is null || _disposed) return;

        try
        {
            // Override wins when active: force the chosen condition regardless
            // of real weather or the user's weather-enabled setting, so dev/staging
            // can see every animation even when no real weather data exists.
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

### Key behavioural points

- **Override short-circuits the early return for disabled weather.** If the user has `WeatherSetting.Enabled = false` or has no location configured, the real weather flow would normally clear the overlay. The override bypasses both checks, so a test device with no location still sees animations. This is essential for the testing use case.
- **`IsWindy` from the override is used directly.** The existing `weather.js` module already handles the `isWindy` flag by adding a `weather-windy` class, so toggling the Windy pill will add or remove that class on the overlay `<div>` via the `OnOverrideChanged` event subscription.
- **`WeatherStrip` is untouched.** It continues to render real data from `IWeatherUiService`. The override is genuinely overlay-only.
- **Subscription cleanup** — `HandleOverrideChanged` is unsubscribed in `DisposeAsync` alongside the existing weather-changed handler.

### Why modify `WeatherOverlay` rather than add a second layer

Adding a second overlay component would mean two `<div id="weather-overlay">` elements fighting for the same JS module. Modifying the existing one keeps the animation pipeline single-sourced: the same `weather.js` handles both real and overridden conditions, so there is no risk of the override animation looking different from the real one.

## Testing strategy

### Unit and bUnit tests (`tests/FamilyHQ.WebUi.Tests/`)

The implementation step will confirm whether this test project already exists and scaffold it if not.

**`WeatherOverrideServiceTests.cs`** (new)

- `Activate_SetsIsActiveAndCondition_FiresEvent`
- `Activate_DefaultsIsWindyToFalse`
- `Activate_WhenAlreadyInSameState_DoesNotFireEvent`
- `SelectCondition_WhileActive_ReplacesCondition_FiresEvent`
- `SelectCondition_WhileInactive_IsNoOp_DoesNotFireEvent`
- `SetWindy_WhileActive_UpdatesFlag_FiresEvent`
- `SetWindy_WhileInactive_IsNoOp_DoesNotFireEvent`
- `Deactivate_ClearsCondition_ResetsIsWindy_FiresEvent`
- `Deactivate_WhileInactive_DoesNotFireEvent`

**`SettingsWeatherOverrideTabTests.cs`** (bUnit)

- Renders the off-state toggle when `IsActive == false`.
- Clicking the toggle invokes `Activate(Clear)` and renders the condition grid and Windy pill.
- Condition grid contains one pill per `WeatherCondition` enum value, with the active one styled `pill-toggle--on`.
- Clicking a condition pill invokes `SelectCondition` with the right value.
- Clicking the Windy pill invokes `SetWindy`.
- Clicking the off toggle while active invokes `Deactivate`.
- Component unsubscribes from `OnOverrideChanged` on dispose.

**`WeatherOverlayTests.cs`** (bUnit, mock `IJSRuntime`)

- When override is inactive, the overlay behaves as before (real weather path verified).
- When override is active with a condition, `setWeatherOverlay` is invoked with that condition string and `isWindy=false` by default.
- When override's `IsWindy` is true, `setWeatherOverlay` is invoked with `isWindy=true`.
- When override is active but `WeatherService.IsEnabled == false`, `setWeatherOverlay` is still invoked (override bypasses the enable check).
- When override deactivates while real weather is present, the next `UpdateOverlay` uses the real condition.
- `DisposeAsync` unsubscribes from both events.

**`SettingsPageTests.cs`** (bUnit — additions to existing or new file)

- With `FeatureFlags.WeatherOverrideEnabled == true`, the Weather Override tab button is rendered.
- With `FeatureFlags.WeatherOverrideEnabled == false`, the tab button is NOT rendered and the switch case is guarded by `when`.

### E2E test (`tests-e2e/FamilyHQ.E2E.Features/WebUi/`)

**`WeatherOverrideDevTool.feature`** (new)

```gherkin
Feature: Weather override dev tool
  As a developer working in dev or staging
  I want to manually force weather animations
  So that I can visually verify each condition's animation without seeding real weather data

  Background:
    Given I am signed in as a fresh test user

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
    Given a current weather condition of "Clear" is seeded for my user
    When I navigate to the Settings page
    And I open the "Weather Override" tab
    And I toggle override on
    And I select the "HeavyRain" condition
    And I toggle override off
    Then the weather overlay element does not have the class "weather-heavyrain"
```

E2E runs target dev and staging where the flag is always `true`, so no tag is needed. Each scenario is independent — sign in, navigate, act, assert, no shared state — per the project's E2E isolation rule.

### Manual / smoke verification

- Run `dotnet run` locally against the WebUi: confirm the tab appears because `appsettings.Development.json` sets the flag true.
- Build the WebUi Docker image and run with `FEATURE_WEATHER_OVERRIDE_ENABLED=true`: confirm the sed substitution landed (`cat /usr/share/nginx/html/appsettings.json` inside the container shows `"FeatureWeatherOverride": true`).
- Run with the env var unset or `false`: confirm `appsettings.json` still reads `false` and the tab is absent.

## File-by-file summary

### New files

- `src/FamilyHQ.WebUi/Configuration/FeatureFlags.cs`
- `src/FamilyHQ.WebUi/Services/IWeatherOverrideService.cs`
- `src/FamilyHQ.WebUi/Services/WeatherOverrideService.cs`
- `src/FamilyHQ.WebUi/Components/Settings/SettingsWeatherOverrideTab.razor`
- `src/FamilyHQ.WebUi/wwwroot/appsettings.Development.json`
- `tests/FamilyHQ.WebUi.Tests/Services/WeatherOverrideServiceTests.cs`
- `tests/FamilyHQ.WebUi.Tests/Components/Settings/SettingsWeatherOverrideTabTests.cs`
- `tests/FamilyHQ.WebUi.Tests/Components/Weather/WeatherOverlayTests.cs`
- `tests/FamilyHQ.WebUi.Tests/Pages/SettingsPageTests.cs` (if not already present; otherwise additions)
- `tests-e2e/FamilyHQ.E2E.Features/WebUi/WeatherOverrideDevTool.feature`
- `tests-e2e/FamilyHQ.E2E.Steps/WeatherOverrideSteps.cs`

### Modified files

- `src/FamilyHQ.WebUi/wwwroot/appsettings.json` — add `"FeatureWeatherOverride": false`.
- `src/FamilyHQ.WebUi/Program.cs` — register `FeatureFlags` singleton and `IWeatherOverrideService` scoped.
- `src/FamilyHQ.WebUi/Pages/Settings.razor` — inject `FeatureFlags`, gate tab button and switch case.
- `src/FamilyHQ.WebUi/Components/Weather/WeatherOverlay.razor` — inject `IWeatherOverrideService`, subscribe, branch in `UpdateOverlay`.
- `src/FamilyHQ.WebUi/wwwroot/css/app.css` — add `.weather-override__toggle-row`, `.weather-override__modifier-row`, and `.weather-override__condition-grid` rules.
- `docker/webui/docker-entrypoint.sh` — add sed substitution for the flag.
- `docker-compose.dev.yml`, `docker-compose.staging.yml`, `docker-compose.preprod.yml`, `docker-compose.prod.yml` — add `FEATURE_WEATHER_OVERRIDE_ENABLED` to the `webui` service environment.
- `.env.dev`, `.env.staging`, `.env.preprod`, `.env.prod`, `.env.example` — add `FEATURE_WEATHER_OVERRIDE_ENABLED` with appropriate values.
- `.agent/docs/architecture.md` — short paragraph documenting the dev override tab and its feature-flag gate. Updated as each implementation step lands, not just in the design.

### Untouched

- All backend projects (`FamilyHQ.WebApi`, `FamilyHQ.Services`, `FamilyHQ.Core`, `FamilyHQ.Data`). No API changes, no user-setting changes, no database changes.
- `WeatherStrip.razor` — still renders real weather unchanged.
- `WeatherService`, `WeatherRefreshService`, `WeatherController`, `OpenMeteoWeatherProvider`.
- User `WeatherSetting` persistence and DTOs.

## Future considerations

- A dedicated preprod/prod test suite (scoped by a future feature) will need to decide whether to explicitly verify the feature-flag-off case — i.e. that the Weather Override tab is genuinely absent on production builds. That decision is deferred to that suite's design, not pre-committed here.
- If additional feature flags accumulate in the WebUi, the current POCO approach should be upgraded to an `IFeatureFlagsService` with a dedicated registration helper. A single flag does not yet justify the abstraction.
