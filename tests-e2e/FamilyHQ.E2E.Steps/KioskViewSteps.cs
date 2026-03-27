using System.Threading.Tasks;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class KioskViewSteps
{
    private readonly ScenarioContext _scenarioContext;

    public KioskViewSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    private IPage Page => _scenarioContext.Get<IPage>();

    [Then(@"I see the event modal")]
    public async Task ThenISeeTheEventModal()
    {
        // The event modal should be visible after clicking a grid slot
        var modal = Page.Locator(".modal-content");
        await Assertions.Expect(modal).ToBeVisibleAsync();
    }

    [Then(@"the ambient background should have a circadian gradient class")]
    public async Task ThenTheAmbientBackgroundShouldHaveACircadianGradientClass()
    {
        // Check that the ambient background has one of the circadian classes
        // The ambient gradient is a div with class ambient-gradient plus one of the state classes
        var ambientGradient = Page.Locator(".ambient-gradient");
        await Assertions.Expect(ambientGradient).ToBeVisibleAsync();
        
        var classList = await ambientGradient.GetAttributeAsync("class");
        classList.Should().ContainAny(
            "ambient-dawn", "ambient-day", "ambient-dusk", "ambient-night",
            "The ambient background should have a circadian gradient class.");
    }

    [Then(@"the ambient background should have the ""([^""]*)"" CSS class")]
    public async Task ThenTheAmbientBackgroundShouldHaveTheCssClass(string expectedClass)
    {
        var ambientGradient = Page.Locator(".ambient-gradient");
        await Assertions.Expect(ambientGradient).ToBeVisibleAsync();
        
        var classList = await ambientGradient.GetAttributeAsync("class");
        classList.Should().Contain(expectedClass,
            $"The ambient background should have the '{expectedClass}' CSS class.");
    }

    [When(@"I wait for the weather to update")]
    public async Task WhenIWaitForTheWeatherToUpdate()
    {
        // Wait for the weather update to propagate via SignalR
        await Task.Delay(1000);
    }
}
