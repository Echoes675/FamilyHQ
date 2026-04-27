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

    [When(@"I click the Sync Now button")]
    public async Task WhenIClickTheSyncNowButton()
    {
        await _settingsPage.ClickSyncNowAsync();
    }

    [Then(@"the Sync Now button is visible")]
    public async Task ThenTheSyncNowButtonIsVisible()
    {
        await Assertions.Expect(_settingsPage.SyncNowBtn)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [When(@"I tap the shared toggle for ""([^""]*)""")]
    public async Task WhenITapTheSharedToggleFor(string calendarName)
    {
        await _settingsPage.GetSharedToggle(calendarName).ClickAsync();
    }

    [Then(@"I see the shared calendar confirmation prompt")]
    public async Task ThenISeeTheSharedCalendarConfirmationPrompt()
    {
        await Assertions.Expect(_settingsPage.SharedChangeConfirmBtn)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [When(@"I cancel the shared calendar confirmation")]
    public async Task WhenICancelTheSharedCalendarConfirmation()
    {
        await _settingsPage.SharedChangeCancelBtn.ClickAsync();
    }

    [Then(@"the visibility toggle for ""([^""]*)"" is disabled")]
    public async Task ThenTheVisibilityToggleIsDisabled(string calendarName)
    {
        var toggle = _settingsPage.GetVisibilityToggle(calendarName);
        (await toggle.IsDisabledAsync()).Should().BeTrue(
            $"The visibility toggle for '{calendarName}' should be disabled because it is the shared calendar.");
    }

    [Then(@"the visibility toggle for ""([^""]*)"" reads ""([^""]*)""")]
    public async Task ThenTheVisibilityToggleReads(string calendarName, string expectedText)
    {
        var toggle = _settingsPage.GetVisibilityToggle(calendarName);
        var actualText = (await toggle.InnerTextAsync()).Trim();
        actualText.Should().Be(expectedText,
            $"The visibility toggle for '{calendarName}' should read '{expectedText}'.");
    }

    [Then(@"the Register Webhooks button is visible")]
    public async Task ThenTheRegisterWebhooksButtonIsVisible()
    {
        await Assertions.Expect(_settingsPage.RegisterWebhooksBtn)
            .ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [When(@"I click the Register Webhooks button")]
    public async Task WhenIClickTheRegisterWebhooksButton()
    {
        await _settingsPage.ClickRegisterWebhooksAsync();
    }

    [Then(@"I see a success message ""([^""]*)""")]
    public async Task ThenISeeASuccessMessage(string expectedMessage)
    {
        var successBanner = _page.Locator(".alert-success").Filter(new() { HasText = expectedMessage });
        await Assertions.Expect(successBanner)
            .ToBeVisibleAsync(new() { Timeout = 10000 });
    }
}
