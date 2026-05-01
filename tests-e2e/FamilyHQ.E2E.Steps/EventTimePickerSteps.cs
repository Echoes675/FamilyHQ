using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class EventTimePickerSteps
{
    private readonly IPage _page;

    public EventTimePickerSteps(ScenarioContext scenarioContext)
    {
        _page = scenarioContext.Get<IPage>();
    }

    private ILocator Picker(string which) =>
        _page.GetByTestId($"{which}-time-picker");

    private async Task<string> ReadDisplayedTimeAsync(string which)
    {
        var picker = Picker(which);
        var displays = picker.Locator(".time-picker-display");
        await displays.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var hour = (await displays.Nth(0).InnerTextAsync()).Trim();
        var minute = (await displays.Nth(1).InnerTextAsync()).Trim();
        return $"{hour}:{minute}";
    }

    [Then(@"the (start|end) time picker shows ""([^""]*)""")]
    public async Task ThenThePickerShows(string which, string expected)
    {
        // Use a polling assertion so the test waits out Blazor re-render after a click.
        var picker = Picker(which);
        var displays = picker.Locator(".time-picker-display");
        await Assertions.Expect(displays.Nth(0))
            .ToHaveTextAsync(expected.Split(':')[0], new() { Timeout = 10000 });
        await Assertions.Expect(displays.Nth(1))
            .ToHaveTextAsync(expected.Split(':')[1], new() { Timeout = 10000 });

        // Final composed read to surface the actual value in any failure message.
        var actual = await ReadDisplayedTimeAsync(which);
        actual.Should().Be(expected);
    }

    [When(@"I press the ""(Increase hour|Decrease hour|Increase minute|Decrease minute)"" button on the (start|end) time picker (\d+) times?")]
    public async Task WhenIPressTheTimePickerButton(string ariaLabel, string which, int count)
    {
        var picker = Picker(which);
        var button = picker.GetByLabel(ariaLabel);
        await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

        for (int i = 0; i < count; i++)
        {
            await button.ClickAsync();
        }
    }
}
