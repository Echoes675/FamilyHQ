using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using FluentAssertions;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class WebhookRegistrationSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public WebhookRegistrationSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [Then(@"a webhook channel is registered for each of the user's calendars")]
    public async Task ThenAWebhookChannelIsRegisteredForEachCalendar()
    {
        var template = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var expectedCalendarCount = template.Calendars.Count;

        // Allow some time for async webhook registration after login
        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<WebhookRegistrationDto> registrations = [];

        while (DateTime.UtcNow < deadline)
        {
            registrations = await _simulatorApi.GetWebhookRegistrationsAsync();
            if (registrations.Count >= expectedCalendarCount)
                break;
            await Task.Delay(500);
        }

        registrations.Should().HaveCount(expectedCalendarCount,
            "each calendar should have a registered webhook channel");

        registrations.Should().AllSatisfy(r =>
        {
            r.ChannelId.Should().NotBeNullOrEmpty();
            r.Address.Should().Contain("/api/sync/webhook");
        });
    }
}
