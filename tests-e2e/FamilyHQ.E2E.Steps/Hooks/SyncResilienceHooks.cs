using FamilyHQ.E2E.Data.Api;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Clears any sync failure modes that scenarios may have injected into the
/// simulator so they cannot leak across the shared simulator process. Runs
/// after every scenario as a defence-in-depth measure even when the scenario
/// itself succeeds and does not throw.
/// </summary>
[Binding]
public class SyncResilienceHooks
{
    private readonly SimulatorApiClient _simulatorApi;

    public SyncResilienceHooks(SimulatorApiClient simulatorApi)
    {
        _simulatorApi = simulatorApi;
    }

    [AfterScenario(Order = 3)]
    public async Task ClearSyncFailureModesAsync()
    {
        try
        {
            await _simulatorApi.ClearAllSyncFailureModesAsync();
        }
        catch
        {
            // The simulator may be unreachable during teardown (e.g. during
            // a failing CI run). Don't mask the real scenario failure.
        }
    }
}
