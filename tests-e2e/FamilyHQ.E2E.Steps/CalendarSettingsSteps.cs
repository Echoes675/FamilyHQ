using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class CalendarSettingsSteps
{
    private readonly SettingsPage _settingsPage;

    public CalendarSettingsSteps(ScenarioContext scenarioContext)
    {
        _settingsPage = new SettingsPage(scenarioContext.Get<IPage>());
    }

    [When(@"I navigate to the calendar settings tab")]
    public async Task WhenINavigateToTheCalendarSettingsTab()
    {
        await _settingsPage.NavigateToCalendarsTabAsync();
    }

    [Then(@"I see ""([^""]*)"" in the calendar settings list")]
    public async Task ThenISeeInTheCalendarSettingsList(string calendarName)
    {
        await Assertions.Expect(
            _settingsPage.GetCalendarSettingsItem(calendarName))
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }

    [When(@"I hide the ""([^""]*)"" calendar")]
    public async Task WhenIHideTheCalendar(string calendarName)
    {
        await _settingsPage.HideCalendarAsync(calendarName);
    }

    [When(@"I hide the ""([^""]*)"" in calendar settings")]
    public async Task WhenIHideInCalendarSettings(string calendarName)
    {
        await _settingsPage.NavigateToCalendarsTabAsync();
        await _settingsPage.HideCalendarAsync(calendarName);
    }

    [When(@"I designate ""([^""]*)"" as the shared calendar")]
    public async Task WhenIDesignateAsTheSharedCalendar(string calendarName)
    {
        await _settingsPage.DesignateSharedCalendarAsync(calendarName);
    }

    [Then(@"""([^""]*)"" is designated as the shared calendar")]
    public async Task ThenIsDesignatedAsTheSharedCalendar(string calendarName)
    {
        var isShared = await _settingsPage.IsCalendarDesignatedSharedAsync(calendarName);
        isShared.Should().BeTrue($"'{calendarName}' should be designated as the shared calendar.");
    }

    [Then(@"""([^""]*)"" is no longer designated as the shared calendar")]
    public async Task ThenIsNoLongerDesignatedAsTheSharedCalendar(string calendarName)
    {
        var isShared = await _settingsPage.IsCalendarDesignatedSharedAsync(calendarName);
        isShared.Should().BeFalse($"'{calendarName}' should not be designated as the shared calendar.");
    }
}
