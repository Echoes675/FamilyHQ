using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Helpers;
using FamilyHQ.E2E.Common.Pages;
using FamilyHQ.E2E.Data.Api;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WeatherSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly DashboardPage _dashboardPage;
    private readonly SettingsPage _settingsPage;
    private readonly WeatherSettingsPage _weatherSettingsPage;
    private readonly SimulatorApiClient _simulatorApi;
    private readonly WebApiClient _webApiClient;

    public WeatherSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
        _webApiClient = new WebApiClient();
        var page = scenarioContext.Get<IPage>();
        _dashboardPage = new DashboardPage(page);
        _settingsPage = new SettingsPage(page);
        _weatherSettingsPage = new WeatherSettingsPage(page);
    }

    // ── Given steps ──────────────────────────────────────────────────────

    [Given(@"the user has a saved location ""([^""]*)"" at ([^,]+), (.+)")]
    public async Task GivenTheUserHasASavedLocation(string placeName, double lat, double lon)
    {
        await _simulatorApi.SetLocationAsync(placeName, lat, lon);

        _scenarioContext["WeatherLatitude"] = lat;
        _scenarioContext["WeatherLongitude"] = lon;
        _scenarioContext["WeatherPlaceName"] = placeName;

        // Navigate to settings, enter place name, save, wait for pill
        await _settingsPage.NavigateAndWaitAsync();
        await _settingsPage.PlaceNameInput.FillAsync(placeName);

        var page = _scenarioContext.Get<IPage>();
        await _settingsPage.SaveLocationBtn.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30000 });
        await _settingsPage.LocationPill.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // Navigate back to dashboard
        await _dashboardPage.NavigateAndWaitAsync();
    }

    [Given(@"weather data is seeded for the location:")]
    public async Task GivenWeatherDataIsSeededForTheLocation(Table table)
    {
        var lat = _scenarioContext.Get<double>("WeatherLatitude");
        var lon = _scenarioContext.Get<double>("WeatherLongitude");
        var row = table.Rows[0];

        var temp = double.Parse(row["Current Temp"]);
        var code = int.Parse(row["Current Code"]);
        var wind = double.Parse(row["Wind Speed"]);

        await _simulatorApi.SetWeatherAsync(new
        {
            Latitude = lat,
            Longitude = lon,
            Current = new
            {
                WeatherCode = code,
                Temperature = temp,
                WindSpeed = wind
            }
        });
    }

    [Given(@"daily forecast data is seeded for the location:")]
    public async Task GivenDailyForecastDataIsSeededForTheLocation(Table table)
    {
        var lat = _scenarioContext.Get<double>("WeatherLatitude");
        var lon = _scenarioContext.Get<double>("WeatherLongitude");

        var dailyItems = table.Rows.Select(row => new
        {
            Date = DateExpressionResolver.Resolve(row["Date"]),
            WeatherCode = int.Parse(row["Code"]),
            TemperatureMax = double.Parse(row["High"]),
            TemperatureMin = double.Parse(row["Low"]),
            WindSpeedMax = double.Parse(row["WindMax"])
        }).ToList();

        await _simulatorApi.SetWeatherAsync(new
        {
            Latitude = lat,
            Longitude = lon,
            Daily = dailyItems
        });
    }

    [Given(@"hourly weather data is seeded for ""([^""]*)"":")]
    public async Task GivenHourlyWeatherDataIsSeededFor(string dateExpr, Table table)
    {
        var lat = _scenarioContext.Get<double>("WeatherLatitude");
        var lon = _scenarioContext.Get<double>("WeatherLongitude");
        var resolvedDate = DateExpressionResolver.Resolve(dateExpr);

        var hourlyItems = table.Rows.Select(row => new
        {
            Time = $"{resolvedDate}T{row["Hour"]}",
            WeatherCode = int.Parse(row["Code"]),
            Temperature = double.Parse(row["Temp"]),
            WindSpeed = double.Parse(row["Wind"])
        }).ToList();

        await _simulatorApi.SetWeatherAsync(new
        {
            Latitude = lat,
            Longitude = lon,
            Hourly = hourlyItems
        });
    }

    // ── When steps ───────────────────────────────────────────────────────

    [When(@"I wait for weather data to load")]
    public async Task WhenIWaitForWeatherDataToLoad()
    {
        await _webApiClient.TriggerWeatherRefreshAsync();

        // Reload the dashboard so WeatherUiService.InitialiseAsync() picks up
        // the freshly-stored data instead of relying on SignalR timing.
        await _dashboardPage.NavigateAndWaitAsync();
        await _dashboardPage.WaitForWeatherStripAsync();
    }

    [When(@"I navigate to weather settings")]
    public async Task WhenINavigateToWeatherSettings()
    {
        await _weatherSettingsPage.NavigateAndWaitAsync();
    }

    [When(@"I disable weather")]
    public async Task WhenIDisableWeather()
    {
        var isChecked = await _weatherSettingsPage.EnabledToggle.IsCheckedAsync();
        if (isChecked)
        {
            await _weatherSettingsPage.EnabledToggle.ClickAsync();
        }
    }

    [When(@"I enable weather")]
    public async Task WhenIEnableWeather()
    {
        var isChecked = await _weatherSettingsPage.EnabledToggle.IsCheckedAsync();
        if (!isChecked)
        {
            await _weatherSettingsPage.EnabledToggle.ClickAsync();
        }
    }

    [When(@"I save weather settings")]
    public async Task WhenISaveWeatherSettings()
    {
        await _weatherSettingsPage.SaveBtn.ClickAsync();
        await _weatherSettingsPage.SuccessMessage.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    [When(@"I change the poll interval to (\d+)")]
    public async Task WhenIChangeThePollIntervalTo(int interval)
    {
        _scenarioContext["OriginalPollInterval"] =
            await _weatherSettingsPage.PollIntervalInput.InputValueAsync();

        // Blazor WASM @onchange only fires from native DOM interactions.
        // Click into the field, select all, type new value, then blur.
        await _weatherSettingsPage.PollIntervalInput.ClickAsync(new() { ClickCount = 3 });
        var page = _scenarioContext.Get<IPage>();
        await page.Keyboard.TypeAsync(interval.ToString());
        await page.Keyboard.PressAsync("Tab");
    }

    [When(@"I click the weather settings link")]
    public async Task WhenIClickTheWeatherSettingsLink()
    {
        await _settingsPage.WeatherSettingsLink.ClickAsync();
        var page = _scenarioContext.Get<IPage>();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [When(@"I click cancel on weather settings")]
    public async Task WhenIClickCancelOnWeatherSettings()
    {
        await _weatherSettingsPage.CancelBtn.ClickAsync();
        var page = _scenarioContext.Get<IPage>();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // ── Then steps ───────────────────────────────────────────────────────

    [Then(@"the weather strip is visible")]
    public async Task ThenTheWeatherStripIsVisible()
    {
        await Assertions.Expect(_dashboardPage.WeatherStrip).ToBeVisibleAsync();
    }

    [Then(@"the weather strip is not visible")]
    public async Task ThenTheWeatherStripIsNotVisible()
    {
        var count = await _dashboardPage.WeatherStrip.CountAsync();
        count.Should().Be(0, "the weather strip should not be visible");
    }

    [Then(@"the weather strip shows a temperature")]
    public async Task ThenTheWeatherStripShowsATemperature()
    {
        var text = await _dashboardPage.WeatherStripTemp.InnerTextAsync();
        text.Should().NotBeEmpty();
        text.Should().Contain("°");
    }

    [Then(@"the weather strip shows condition ""([^""]*)""")]
    public async Task ThenTheWeatherStripShowsCondition(string expectedText)
    {
        var text = await _dashboardPage.WeatherStripCondition.InnerTextAsync();
        text.Should().Contain(expectedText);
    }

    [Then(@"I see forecast days in the weather strip")]
    public async Task ThenISeeForecaseDaysInTheWeatherStrip()
    {
        var count = await _dashboardPage.WeatherStripForecastDays.CountAsync();
        count.Should().BeGreaterThan(0, "at least one forecast day should be displayed");
    }

    [Then(@"the weather overlay has class ""([^""]*)""")]
    public async Task ThenTheWeatherOverlayHasClass(string className)
    {
        var classes = await _dashboardPage.WeatherOverlay.GetAttributeAsync("class") ?? "";
        classes.Should().Contain(className);
    }

    [Then(@"the weather overlay has no condition class")]
    public async Task ThenTheWeatherOverlayHasNoConditionClass()
    {
        var classes = await _dashboardPage.WeatherOverlay.GetAttributeAsync("class") ?? "";
        classes.Should().NotMatchRegex(@"weather-\w+",
            "the weather overlay should not have any weather condition class");
    }

    [Then(@"the agenda row for ""([^""]*)"" shows weather temperatures")]
    public async Task ThenTheAgendaRowForShowsWeatherTemperatures(string dateExpr)
    {
        var dateKey = DateExpressionResolver.Resolve(dateExpr);
        await Assertions.Expect(_dashboardPage.AgendaWeatherTemps(dateKey))
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [Then(@"I see hourly temperatures in the day view")]
    public async Task ThenISeeHourlyTemperaturesInTheDayView()
    {
        var count = await _dashboardPage.DayHourTemps.CountAsync();
        count.Should().BeGreaterThan(0, "at least one hourly temperature should be displayed");
    }

    [Then(@"I am on the weather settings page")]
    public async Task ThenIAmOnTheWeatherSettingsPage()
    {
        var page = _scenarioContext.Get<IPage>();
        page.Url.Should().Contain("/settings/weather");
        await Assertions.Expect(_weatherSettingsPage.EnabledToggle).ToBeVisibleAsync();
    }

    [Then(@"I see the weather enabled toggle")]
    public async Task ThenISeeTheWeatherEnabledToggle()
    {
        await Assertions.Expect(_weatherSettingsPage.EnabledToggle).ToBeVisibleAsync();
    }

    [Then(@"I see the temperature unit selector")]
    public async Task ThenISeeTheTemperatureUnitSelector()
    {
        await Assertions.Expect(_weatherSettingsPage.TemperatureUnitSelect).ToBeVisibleAsync();
    }

    [Then(@"I see the poll interval input")]
    public async Task ThenISeeThePollIntervalInput()
    {
        await Assertions.Expect(_weatherSettingsPage.PollIntervalInput).ToBeVisibleAsync();
    }

    [Then(@"I see the wind threshold input")]
    public async Task ThenISeeTheWindThresholdInput()
    {
        await Assertions.Expect(_weatherSettingsPage.WindThresholdInput).ToBeVisibleAsync();
    }

    [Then(@"the save button is visible")]
    public async Task ThenTheSaveButtonIsVisible()
    {
        await Assertions.Expect(_weatherSettingsPage.SaveBtn).ToBeVisibleAsync();
    }

    [Then(@"the save button is not visible")]
    public async Task ThenTheSaveButtonIsNotVisible()
    {
        var count = await _weatherSettingsPage.SaveBtn.CountAsync();
        count.Should().Be(0, "the save button should not be visible");
    }

    [Then(@"the cancel button is visible")]
    public async Task ThenTheCancelButtonIsVisible()
    {
        await Assertions.Expect(_weatherSettingsPage.CancelBtn).ToBeVisibleAsync();
    }

    [Then(@"the poll interval shows the original value")]
    public async Task ThenThePollIntervalShowsTheOriginalValue()
    {
        var original = _scenarioContext.TryGetValue("OriginalPollInterval", out string? stored)
            ? stored ?? "5"
            : "5";

        var current = await _weatherSettingsPage.PollIntervalInput.InputValueAsync();
        current.Should().Be(original);
    }

    [Then(@"I see the ""([^""]*)"" confirmation")]
    public async Task ThenISeeTheConfirmation(string expectedText)
    {
        var message = _weatherSettingsPage.SuccessMessage;
        await message.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var text = await message.InnerTextAsync();
        text.Should().Contain(expectedText);
    }

    // ── Cleanup ──────────────────────────────────────────────────────────

    [AfterScenario]
    public async Task CleanupWeatherData()
    {
        if (_scenarioContext.TryGetValue("WeatherLatitude", out double lat) &&
            _scenarioContext.TryGetValue("WeatherLongitude", out double lon))
        {
            await _simulatorApi.ClearWeatherAsync(lat, lon);
        }

        if (_scenarioContext.TryGetValue("WeatherPlaceName", out string? placeName) && placeName != null)
        {
            await _simulatorApi.ClearLocationAsync(placeName);
        }
    }
}
