using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Clears any sync failure mode this scenario injected into the simulator so
/// it cannot leak across the shared simulator process. Runs after every
/// scenario as defence-in-depth even when the scenario itself succeeds.
///
/// FHQ-31: this hook used to call ClearAllSyncFailureModesAsync, which wipes
/// every user's failure mode in one shot. With xunit running scenarios in
/// parallel (xunit.runner.json: maxParallelThreads=2), Scenario A's teardown
/// could fire mid-flight against Scenario B and erase a failure mode that B
/// had just set but not yet exercised. B's sync then went through without
/// hitting the Google reauth path, the user was never marked NeedsReauth,
/// and the test asserted on a stale "Active" badge. ~25% flake rate observed
/// across Deploy-Staging #102, #103, #109, #110, #112 on the SyncResilience
/// scenarios that share the RefreshTokenInvalidGrant setup. Per-user clear,
/// keyed by this scenario's UserTemplate.UserName, is parallel-safe.
/// </summary>
[Binding]
public class SyncResilienceHooks
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public SyncResilienceHooks(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [AfterScenario(Order = 3)]
    public async Task ClearSyncFailureModesAsync()
    {
        // Only this scenario's user. ClearAll would race against parallel
        // scenarios — see class comment.
        if (!_scenarioContext.TryGetValue<SimulatorConfigurationModel>("UserTemplate", out var template))
            return;

        try
        {
            await _simulatorApi.ClearSyncFailureModeAsync(template.UserName);
        }
        catch
        {
            // The simulator may be unreachable during teardown (e.g. during
            // a failing CI run). Don't mask the real scenario failure.
        }
    }
}
