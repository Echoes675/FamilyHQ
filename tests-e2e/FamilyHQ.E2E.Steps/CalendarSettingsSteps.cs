using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class CalendarSettingsSteps
{
    private readonly SettingsPage _settingsPage;
    private readonly IPage _page;

    public CalendarSettingsSteps(ScenarioContext scenarioContext)
    {
        _page = scenarioContext.Get<IPage>();
        _settingsPage = new SettingsPage(_page);
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

    [Then(@"the ""([^""]*)"" chip is available in the modal")]
    public async Task ThenTheChipIsAvailableInTheModal(string calendarName)
    {
        var chip = _page.Locator(".modal-content .chip").Filter(new() { HasText = calendarName });
        await Assertions.Expect(chip).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Then(@"the ""([^""]*)"" chip is not available in the modal")]
    public async Task ThenTheChipIsNotAvailableInTheModal(string calendarName)
    {
        var chip = _page.Locator(".modal-content .chip").Filter(new() { HasText = calendarName });
        (await chip.CountAsync()).Should().Be(0,
            $"The '{calendarName}' chip should not be selectable in the event modal.");
    }
}
