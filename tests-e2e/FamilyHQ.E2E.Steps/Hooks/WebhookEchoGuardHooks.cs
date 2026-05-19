using FamilyHQ.E2E.Data.Api;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Resets the Simulator's outbound write-count store after each WebhookEchoGuard
/// scenario so counts from one scenario cannot leak into the next.
/// </summary>
[Binding]
public class WebhookEchoGuardHooks
{
    private readonly SimulatorApiClient _simulatorApi;

    public WebhookEchoGuardHooks(SimulatorApiClient simulatorApi)
    {
        _simulatorApi = simulatorApi;
    }

    [AfterScenario("WebhookEchoGuard", Order = 4)]
    public async Task ResetWriteCountsAsync()
    {
        try
        {
            await _simulatorApi.ResetOutboundWriteCountsAsync();
        }
        catch
        {
            // Do not mask scenario failures — teardown is best-effort.
        }
    }
}
