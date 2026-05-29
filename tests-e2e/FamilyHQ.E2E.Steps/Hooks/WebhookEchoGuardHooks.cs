using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Resets the Simulator's outbound write-count for THIS scenario's isolated user after the
/// scenario. Write counts are keyed by unique (Guid-based) event IDs and per isolated user, so they
/// cannot leak between scenarios and need no before-scenario baseline. A GLOBAL reset under the
/// parallel runner would wipe a concurrent scenario's counts mid-flight — the FHQ-31 ClearAll race —
/// so the teardown is scoped to the scenario's own user only.
/// </summary>
[Binding]
public class WebhookEchoGuardHooks
{
    private readonly SimulatorApiClient _simulatorApi;
    private readonly ScenarioContext _scenarioContext;

    public WebhookEchoGuardHooks(SimulatorApiClient simulatorApi, ScenarioContext scenarioContext)
    {
        _simulatorApi = simulatorApi;
        _scenarioContext = scenarioContext;
    }

    [AfterScenario("WebhookEchoGuard", Order = 4)]
    public async Task ResetWriteCountsAfterAsync()
    {
        // Only the current scenario's user; skip if the user was never provisioned (early failure).
        if (!_scenarioContext.TryGetValue<SimulatorConfigurationModel>("UserTemplate", out var template)
            || string.IsNullOrEmpty(template.UserName))
        {
            return;
        }

        try
        {
            await _simulatorApi.ResetUserOutboundWriteCountsAsync(template.UserName);
        }
        catch
        {
            // Do not mask scenario failures — teardown is best-effort.
        }
    }
}
