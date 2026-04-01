# UI Fixes & Settings E2E Tests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix Settings back navigation, month view horizontal overflow, Bootstrap button theming, and add a full E2E acceptance test suite for the Settings page.

**Architecture:** All changes are CSS/Razor only — no new backend code, no new services, no new DTOs. Three app.css edits handle the visual fixes; DashboardHeader gains a `BackUrl` parameter; Settings.razor gains an auth guard; three new E2E files cover the Settings page scenarios.

**Tech Stack:** .NET 10, Blazor WASM, Bootstrap 5 (local), CSS custom properties, Reqnroll/Playwright E2E tests, FluentAssertions.

---

## File Map

| File | Change |
|---|---|
| `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor` | Add `BackUrl` parameter; render back arrow when set |
| `src/FamilyHQ.WebUi/Pages/Settings.razor` | Add `<DashboardHeader BackUrl="/" />` and auth guard |
| `src/FamilyHQ.WebUi/wwwroot/css/app.css` | Add `.dashboard-header__back`, `table-layout: fixed`, `overflow-x: hidden`, Bootstrap button overrides |
| `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs` | New POM for Settings page |
| `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature` | New BDD feature file — 6 authenticated scenarios |
| `tests-e2e/FamilyHQ.E2E.Features/WebUi/Authentication.feature` | Add unauthenticated Settings redirect scenario |
| `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs` | New step bindings for Settings page |

---

## Task 1: Settings back button and auth guard

**Files:**
- Modify: `src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor`
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`
- Modify: `src/FamilyHQ.WebUi/Pages/Settings.razor`

### Context

`DashboardHeader.razor` currently renders a hardcoded `FamilyHQ` brand and a settings gear icon. The Settings page has no navigation back to the dashboard, and an unauthenticated user navigating directly to `/settings` will crash with a 401 from the API.

The fix: add an optional `BackUrl` parameter to `DashboardHeader`. When set, it renders a left-chevron back link and a "Settings" title instead of the brand + gear. Settings.razor uses it with `BackUrl="/"` and adds an auth guard.

- [ ] **Step 1: Update DashboardHeader.razor**

Replace the entire file with:

```razor
@using Microsoft.AspNetCore.Components.Routing

<header class="dashboard-header">
    @if (BackUrl is null)
    {
        <span class="dashboard-header__brand">FamilyHQ</span>
        <a href="/settings" class="dashboard-header__settings" aria-label="Settings">
            <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24"
                 fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="3"/>
                <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06
                         a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09
                         A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83
                         l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09
                         A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83
                         l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09
                         a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83
                         l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09
                         a1.65 1.65 0 0 0-1.51 1z"/>
            </svg>
        </a>
    }
    else
    {
        <div class="d-flex align-items-center gap-2">
            <a href="@BackUrl" class="dashboard-header__back" aria-label="Back">
                <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24"
                     fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <polyline points="15 18 9 12 15 6"></polyline>
                </svg>
            </a>
            <span class="dashboard-header__brand">Settings</span>
        </div>
    }
</header>

@code {
    [Parameter] public string? BackUrl { get; set; }
}
```

- [ ] **Step 2: Add `.dashboard-header__back` CSS to app.css**

Append after the `.dashboard-header__settings:hover` rule (after line 611 of `src/FamilyHQ.WebUi/wwwroot/css/app.css`):

```css
.dashboard-header__back {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 48px;
    height: 48px;
    color: var(--theme-text);
    text-decoration: none;
    border-radius: 50%;
    transition: background-color 0.2s ease, color 45s ease-in-out;
}

.dashboard-header__back:hover {
    background: var(--theme-accent);
    color: var(--theme-on-accent);
}
```

- [ ] **Step 3: Update Settings.razor — add DashboardHeader and auth guard**

Replace the opening of `src/FamilyHQ.WebUi/Pages/Settings.razor` to add `<DashboardHeader BackUrl="/" />` before the settings-page div and the auth guard at the start of `OnInitializedAsync`.

The new file content:

```razor
@page "/settings"
@using FamilyHQ.Core.DTOs
@using FamilyHQ.WebUi.Services
@using FamilyHQ.WebUi.Services.Auth
@inject ISettingsApiService SettingsApiService
@inject IAuthenticationService AuthService
@inject IJSRuntime JS
@inject NavigationManager Navigation

<DashboardHeader BackUrl="/" />

<div class="settings-page">

    <h2 class="settings-section__title">Location</h2>
    <div class="settings-section">
        @if (_locationSetting is not null)
        {
            <div class="settings-location-pill">
                <span>@_locationSetting.PlaceName</span>
                <span class="settings-location-pill__badge @(_locationSetting.IsAutoDetected ? "badge--auto" : "badge--saved")">
                    @(_locationSetting.IsAutoDetected ? "Auto" : "Saved")
                </span>
            </div>
        }
        else
        {
            <p class="settings-hint">No location saved — using automatic detection.</p>
        }

        <div class="settings-input-container" @ref="_scrollContainer">
            <label for="place-input" class="settings-label">Place name</label>
            <input id="place-input"
                   type="text"
                   class="settings-input"
                   @bind="_placeNameInput"
                   @bind:event="oninput"
                   @onfocus="OnInputFocus"
                   placeholder="e.g. Edinburgh, Scotland" />
        </div>

        @if (!string.IsNullOrWhiteSpace(_saveError))
        {
            <p class="settings-error">@_saveError</p>
        }

        <button class="settings-btn" data-testid="save-location-btn" @onclick="SaveLocationAsync" disabled="@_isSaving">
            @(_isSaving ? "Saving…" : (_locationSetting is { IsAutoDetected: false } ? "Update Location" : "Save Location"))
        </button>
        <p class="settings-hint">Takes effect immediately.</p>
    </div>

    <h2 class="settings-section__title">Today's Theme Schedule</h2>
    <div class="settings-section settings-theme-schedule">
        @if (_todayTheme is not null)
        {
            <div class="theme-tile theme-tile--morning">
                <span class="theme-tile__name">Morning</span>
                <span class="theme-tile__time">@_todayTheme.MorningStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--daytime">
                <span class="theme-tile__name">Daytime</span>
                <span class="theme-tile__time">@_todayTheme.DaytimeStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--evening">
                <span class="theme-tile__name">Evening</span>
                <span class="theme-tile__time">@_todayTheme.EveningStart.ToString("HH:mm")</span>
            </div>
            <div class="theme-tile theme-tile--night">
                <span class="theme-tile__name">Night</span>
                <span class="theme-tile__time">@_todayTheme.NightStart.ToString("HH:mm")</span>
            </div>
        }
    </div>

    <h2 class="settings-section__title">Account</h2>
    <div class="settings-section settings-account">
        @if (_username is not null)
        {
            <div class="account-avatar">@GetInitials(_username)</div>
            <p class="account-name">@_username</p>
        }
        <button class="settings-btn settings-btn--secondary" @onclick="OnSignOutClicked">Sign Out</button>
    </div>

</div>

@code {
    private LocationSettingDto? _locationSetting;
    private DayThemeDto? _todayTheme;
    private string? _username;
    private string _placeNameInput = string.Empty;
    private bool _isSaving;
    private string? _saveError;
    private ElementReference _scrollContainer;

    protected override async Task OnInitializedAsync()
    {
        if (!await AuthService.IsAuthenticatedAsync())
        {
            Navigation.NavigateTo("/");
            return;
        }

        _username = await AuthService.GetUsernameAsync();
        _locationSetting = await SettingsApiService.GetLocationAsync();
        _todayTheme = await SettingsApiService.GetTodayThemeAsync();
    }

    private async Task OnInputFocus()
    {
        await JS.InvokeVoidAsync("eval",
            "document.getElementById('place-input')?.scrollIntoView({ behavior: 'smooth', block: 'center' })");
    }

    private async Task SaveLocationAsync()
    {
        if (string.IsNullOrWhiteSpace(_placeNameInput)) return;

        _isSaving = true;
        _saveError = null;
        try
        {
            _locationSetting = await SettingsApiService.SaveLocationAsync(_placeNameInput);
            _placeNameInput = string.Empty;
            _todayTheme = await SettingsApiService.GetTodayThemeAsync();
        }
        catch (Exception ex)
        {
            _saveError = $"Could not save location: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task OnSignOutClicked()
    {
        await AuthService.SignOutAsync();
        Navigation.NavigateTo("/", forceLoad: true);
    }

    private static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
            : name[..Math.Min(2, name.Length)].ToUpperInvariant();
    }
}
```

- [ ] **Step 4: Build to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add src/FamilyHQ.WebUi/Components/Dashboard/DashboardHeader.razor \
        src/FamilyHQ.WebUi/Pages/Settings.razor \
        src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(ui): add settings back button and auth guard to settings page"
```

---

## Task 2: Month view overflow fix

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

### Context

`.month-table` in `app.css` has `width: 100%` but no `table-layout: fixed`. Without it, table cells ignore their declared `width: 14.28%` (set on `<th>` in MonthView.razor) and expand to fit content. Long event titles push the table wider than the viewport at portrait 1080p.

Note: `.event-capsule` already has `white-space: nowrap; overflow: hidden; text-overflow: ellipsis;` (lines 160–162 of app.css) — no change needed there.

- [ ] **Step 1: Add `table-layout: fixed` to `.month-table`**

In `src/FamilyHQ.WebUi/wwwroot/css/app.css`, find the `.month-table` rule (around line 119) and add `table-layout: fixed`:

```css
.month-table {
    width: 100%;
    table-layout: fixed;
    border-collapse: collapse;
    background: var(--theme-surface);
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06);
    border-radius: 8px;
    overflow: hidden;
    transition: background-color 45s ease-in-out, color 45s ease-in-out, border-color 45s ease-in-out;
}
```

- [ ] **Step 2: Add `overflow-x: hidden` to `.dashboard-container`**

In `src/FamilyHQ.WebUi/wwwroot/css/app.css`, find the `.dashboard-container` rule (around line 113) and add `overflow-x: hidden`:

```css
.dashboard-container {
    max-width: 1400px;
    margin: 0 auto;
    padding: 2rem;
    overflow-x: hidden;
}
```

- [ ] **Step 3: Build to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "fix(ui): prevent month table horizontal overflow with table-layout fixed"
```

---

## Task 3: Bootstrap button theming

**Files:**
- Modify: `src/FamilyHQ.WebUi/wwwroot/css/app.css`

### Context

Bootstrap 5 is loaded from `lib/bootstrap/dist/css/bootstrap.min.css` before `app.css`. Bootstrap 5 uses `--bs-btn-*` CSS custom properties for button colours. Overriding these per class in `app.css` is the correct approach — it participates in the CSS cascade without `!important`.

Buttons that need theming: `.btn-primary` (Add Event, modal Save), `.btn-outline-primary` (Prev/Next month), `.btn-light` (month name button), `.btn-secondary` (modal Cancel). `.btn-outline-danger` (modal Delete) is intentionally excluded.

All overrides include `transition` to participate in the 45-second theme crossfade.

- [ ] **Step 1: Append Bootstrap button overrides to app.css**

Append to the end of `src/FamilyHQ.WebUi/wwwroot/css/app.css`:

```css
/* =============================================
   Bootstrap button theme overrides
   Bootstrap 5 CSS custom properties are overridden here.
   app.css loads after bootstrap.min.css so these cascade correctly.
   All colour transitions participate in the 45-second theme crossfade.
   .btn-outline-danger is intentionally excluded (red = destructive intent).
   ============================================= */

.btn-primary {
    --bs-btn-color: var(--theme-on-accent);
    --bs-btn-bg: var(--theme-accent);
    --bs-btn-border-color: var(--theme-accent);
    --bs-btn-hover-color: var(--theme-on-accent);
    --bs-btn-hover-bg: var(--theme-accent-hover);
    --bs-btn-hover-border-color: var(--theme-accent-hover);
    --bs-btn-active-color: var(--theme-on-accent);
    --bs-btn-active-bg: var(--theme-accent-hover);
    --bs-btn-active-border-color: var(--theme-accent-hover);
    --bs-btn-disabled-color: var(--theme-on-accent);
    --bs-btn-disabled-bg: var(--theme-accent);
    --bs-btn-disabled-border-color: var(--theme-accent);
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out,
                color 45s ease-in-out, box-shadow .15s ease-in-out;
}

.btn-outline-primary {
    --bs-btn-color: var(--theme-accent);
    --bs-btn-border-color: var(--theme-accent);
    --bs-btn-hover-color: var(--theme-on-accent);
    --bs-btn-hover-bg: var(--theme-accent);
    --bs-btn-hover-border-color: var(--theme-accent);
    --bs-btn-active-color: var(--theme-on-accent);
    --bs-btn-active-bg: var(--theme-accent);
    --bs-btn-active-border-color: var(--theme-accent);
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out,
                color 45s ease-in-out, box-shadow .15s ease-in-out;
}

.btn-light {
    --bs-btn-color: var(--theme-text);
    --bs-btn-bg: var(--theme-surface);
    --bs-btn-border-color: var(--theme-border);
    --bs-btn-hover-color: var(--theme-text);
    --bs-btn-hover-bg: var(--theme-surface);
    --bs-btn-hover-border-color: var(--theme-accent);
    --bs-btn-active-color: var(--theme-text);
    --bs-btn-active-bg: var(--theme-surface);
    --bs-btn-active-border-color: var(--theme-accent);
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out,
                color 45s ease-in-out, box-shadow .15s ease-in-out;
}

.btn-secondary {
    --bs-btn-color: var(--theme-text-muted);
    --bs-btn-bg: var(--theme-surface);
    --bs-btn-border-color: var(--theme-border);
    --bs-btn-hover-color: var(--theme-text);
    --bs-btn-hover-bg: var(--theme-surface);
    --bs-btn-hover-border-color: var(--theme-border);
    --bs-btn-active-color: var(--theme-text);
    --bs-btn-active-bg: var(--theme-surface);
    --bs-btn-active-border-color: var(--theme-border);
    transition: background-color 45s ease-in-out, border-color 45s ease-in-out,
                color 45s ease-in-out, box-shadow .15s ease-in-out;
}
```

- [ ] **Step 2: Build to verify no errors**

```bash
dotnet build src/FamilyHQ.WebUi/FamilyHQ.WebUi.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/FamilyHQ.WebUi/wwwroot/css/app.css
git commit -m "feat(ui): theme Bootstrap buttons with ambient CSS variables"
```

---

## Task 4: Settings E2E tests

**Files:**
- Create: `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`
- Create: `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature`
- Create: `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs`
- Modify: `tests-e2e/FamilyHQ.E2E.Features/WebUi/Authentication.feature`

### Context

The Settings page is live at `/settings`. The test infrastructure uses Reqnroll (BDD/Gherkin), Playwright, and FluentAssertions. Step definitions are in `FamilyHQ.E2E.Steps`, page objects in `FamilyHQ.E2E.Common/Pages`, and feature files in `FamilyHQ.E2E.Features/WebUi`.

Existing reusable steps (do NOT redefine these):
- `[Given(@"I have a user like ""([^""]*)""")]` — in `UserSteps.cs`
- `[Given(@"I am signed in as the user ""([^""]*)""")]` — in `AuthenticationSteps.cs`
- `[Given(@"I am not authenticated")]` — in `AuthenticationSteps.cs`
- `[Then(@"I see the ""([^""]*)"" button")]` — in `AuthenticationSteps.cs`
- `[Then(@"I see the calendar displayed")]` — in `AuthenticationSteps.cs`

`Settings.feature` uses a `Background` block for the three shared preconditions (user + sign-in + on settings page) because every scenario in this file requires them. The unauthenticated redirect scenario goes into `Authentication.feature` because it tests auth behaviour, not Settings page content.

- [ ] **Step 1: Create `SettingsPage.cs`**

Create `tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs`:

```csharp
using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public class SettingsPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/settings";

    public SettingsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    public ILocator BackBtn => Page.Locator(".dashboard-header__back");
    public ILocator LocationHint => Page.Locator(".settings-hint").Filter(new() { HasText = "No location saved" });
    public ILocator LocationPill => Page.Locator(".settings-location-pill");
    public ILocator LocationPillBadge => Page.Locator(".settings-location-pill__badge");
    public ILocator PlaceNameInput => Page.Locator("#place-input");
    public ILocator SaveLocationBtn => Page.GetByTestId("save-location-btn");
    public ILocator MorningTile => Page.Locator(".theme-tile--morning");
    public ILocator DaytimeTile => Page.Locator(".theme-tile--daytime");
    public ILocator EveningTile => Page.Locator(".theme-tile--evening");
    public ILocator NightTile => Page.Locator(".theme-tile--night");
    public ILocator AccountName => Page.Locator(".account-name");
    public ILocator SignOutBtn => Page.Locator(".settings-account")
        .GetByRole(AriaRole.Button, new() { Name = "Sign Out" });

    public async Task NavigateAndWaitAsync()
    {
        await NavigateAsync();
        await Page.Locator(".settings-page").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    }
}
```

- [ ] **Step 2: Create `Settings.feature`**

Create `tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature`:

```gherkin
Feature: Settings Page
  As an authenticated user
  I want to manage my settings
  So that I can configure my location, view my theme schedule, and sign out

  Background:
    Given I have a user like "TestFamilyMember"
    And I am signed in as the user "TestFamilyMember"
    And I am on the settings page

  Scenario: Back button returns user to the dashboard
    When I click the back button
    Then I see the calendar displayed

  Scenario: Location section shows no saved location hint
    Then I see the no saved location hint

  Scenario: User can save a location
    When I enter "Edinburgh, Scotland" as the place name
    And I click save location
    Then I see the location pill displaying "Edinburgh, Scotland"
    And I see the "Saved" badge on the location pill

  Scenario: Theme schedule shows all four time-of-day periods
    Then I see the Morning theme tile with a time
    And I see the Daytime theme tile with a time
    And I see the Evening theme tile with a time
    And I see the Night theme tile with a time

  Scenario: Account section shows the signed-in username
    Then I see the username in the account section

  Scenario: User can sign out from the settings page
    When I click the sign out button on the settings page
    Then I see the "Login to Google" button
```

- [ ] **Step 3: Create `SettingsSteps.cs`**

Create `tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs`:

```csharp
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class SettingsSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SettingsPage _settingsPage;
    private readonly DashboardPage _dashboardPage;

    public SettingsSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        var page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(page);
        _dashboardPage = new DashboardPage(page);
    }

    [Given(@"I am on the settings page")]
    public async Task GivenIAmOnTheSettingsPage()
    {
        await _settingsPage.NavigateAndWaitAsync();
    }

    [When(@"I navigate to the settings page")]
    public async Task WhenINavigateToTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        var config = ConfigurationLoader.Load();
        await page.GotoAsync(config.BaseUrl + "/settings");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"I click the back button")]
    public async Task WhenIClickTheBackButton()
    {
        await _settingsPage.BackBtn.ClickAsync();
    }

    [When(@"I enter ""([^""]*)"" as the place name")]
    public async Task WhenIEnterAsThePlaceName(string placeName)
    {
        await _settingsPage.PlaceNameInput.FillAsync(placeName);
    }

    [When(@"I click save location")]
    public async Task WhenIClickSaveLocation()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SaveLocationBtn.ClickAsync();
        // Geocoding API call may take a few seconds
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
    }

    [When(@"I click the sign out button on the settings page")]
    public async Task WhenIClickTheSignOutButtonOnTheSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SignOutBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Then(@"I see the no saved location hint")]
    public async Task ThenISeeTheNoSavedLocationHint()
    {
        await _settingsPage.LocationHint.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [Then(@"I see the location pill displaying ""([^""]*)""")]
    public async Task ThenISeeTheLocationPillDisplaying(string placeName)
    {
        await _settingsPage.LocationPill.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        var text = await _settingsPage.LocationPill.InnerTextAsync();
        text.Should().Contain(placeName);
    }

    [Then(@"I see the ""([^""]*)"" badge on the location pill")]
    public async Task ThenISeeTheBadgeOnTheLocationPill(string badgeText)
    {
        var text = await _settingsPage.LocationPillBadge.InnerTextAsync();
        text.Trim().Should().Be(badgeText);
    }

    [Then(@"I see the Morning theme tile with a time")]
    public async Task ThenISeeTheMorningThemeTileWithATime()
    {
        await _settingsPage.MorningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.MorningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Morning tile should display a time");
    }

    [Then(@"I see the Daytime theme tile with a time")]
    public async Task ThenISeeTheDaytimeThemeTileWithATime()
    {
        await _settingsPage.DaytimeTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.DaytimeTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Daytime tile should display a time");
    }

    [Then(@"I see the Evening theme tile with a time")]
    public async Task ThenISeeTheEveningThemeTileWithATime()
    {
        await _settingsPage.EveningTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.EveningTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Evening tile should display a time");
    }

    [Then(@"I see the Night theme tile with a time")]
    public async Task ThenISeeTheNightThemeTileWithATime()
    {
        await _settingsPage.NightTile.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var time = await _settingsPage.NightTile.Locator(".theme-tile__time").InnerTextAsync();
        time.Trim().Should().NotBeEmpty("Night tile should display a time");
    }

    [Then(@"I see the username in the account section")]
    public async Task ThenISeeTheUsernameInTheAccountSection()
    {
        await _settingsPage.AccountName.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var text = await _settingsPage.AccountName.InnerTextAsync();
        text.Trim().Should().NotBeEmpty("Account section should display the signed-in username");
    }
}
```

- [ ] **Step 4: Add unauthenticated Settings scenario to Authentication.feature**

Append this scenario to `tests-e2e/FamilyHQ.E2E.Features/WebUi/Authentication.feature` (after the last existing scenario):

```gherkin
  Scenario: Settings page requires authentication
    Given I am not authenticated
    When I navigate to the settings page
    Then I see the "Login to Google" button
```

- [ ] **Step 5: Build the full solution to verify no errors**

```bash
dotnet build FamilyHQ.slnx
```

Expected: `Build succeeded. 0 Error(s)` (the pre-existing code-behind warning is acceptable).

- [ ] **Step 6: Commit**

```bash
git add tests-e2e/FamilyHQ.E2E.Common/Pages/SettingsPage.cs \
        tests-e2e/FamilyHQ.E2E.Features/WebUi/Settings.feature \
        tests-e2e/FamilyHQ.E2E.Features/WebUi/Authentication.feature \
        tests-e2e/FamilyHQ.E2E.Steps/SettingsSteps.cs
git commit -m "test(e2e): add Settings page acceptance tests"
```

---

## Task 5: CI gate — push, build, deploy, verify E2E

**Files:** none (CI-only)

- [ ] **Step 1: Push the branch**

```bash
git push origin feature/ui-redesign-2
```

- [ ] **Step 2: Trigger and watch the Jenkins build**

```bash
jk run start "FamilyHQ/feature%2Fui-redesign-2" --follow --timeout 10m
```

Expected: `Result: SUCCESS`

- [ ] **Step 3: Trigger the deploy pipeline**

```bash
jk run start "FamilyHQ-Deploy-Dev" -p "BRANCH=feature/ui-redesign-2" --follow --timeout 15m
```

Expected: `Result: SUCCESS`

- [ ] **Step 4: Confirm all E2E tests pass**

```bash
jk log "FamilyHQ-Deploy-Dev" <run-number> 2>&1 | grep -E "Total tests:|failed="
```

Expected output contains: `Total tests: 71` (64 existing + 7 new: 6 in Settings.feature + 1 in Authentication.feature) and `failed=0`.

If any tests fail, read the full log to diagnose, fix, and re-run from Step 1.
