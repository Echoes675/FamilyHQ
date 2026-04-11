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

    [When(@"I open the ""([^""]*)"" tab")]
    public async Task WhenIOpenTheTab(string tabName)
    {
        if (tabName == "Weather Override")
            await _settingsPage.NavigateToWeatherOverrideTabAsync();
        else
            throw new NotSupportedException($"Tab '{tabName}' is not supported by this step.");
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

    [When(@"I select the ""([^""]*)"" condition")]
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

    [Then(@"the weather overlay element has the class ""([^""]*)""")]
    public async Task ThenTheWeatherOverlayElementHasTheClass(string className)
    {
        await Assertions.Expect(_dashboardPage.WeatherOverlay).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(className),
            new() { Timeout = 10000 });
    }

    [Then(@"the weather overlay element does not have the class ""([^""]*)""")]
    public async Task ThenTheWeatherOverlayElementDoesNotHaveTheClass(string className)
    {
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
