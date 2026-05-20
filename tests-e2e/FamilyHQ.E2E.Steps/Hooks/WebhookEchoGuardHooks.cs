using FamilyHQ.E2E.Data.Api;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Resets the Simulator's outbound write-count store before and after each
/// WebhookEchoGuard scenario so counts from one scenario cannot leak into the next,
/// and so every scenario starts with a clean baseline regardless of prior run state.
/// </summary>
[Binding]
public class WebhookEchoGuardHooks
{
    private readonly SimulatorApiClient _simulatorApi;

    public WebhookEchoGuardHooks(SimulatorApiClient simulatorApi)
    {
        _simulatorApi = simulatorApi;
    }

    [BeforeScenario("WebhookEchoGuard", Order = 4)]
    public async Task ResetWriteCountsBeforeAsync()
    {
        try
        {
            await _simulatorApi.ResetOutboundWriteCountsAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to reset write counts before scenario — Simulator may be unavailable: {ex.Message}", ex);
        }
    }

    [AfterScenario("WebhookEchoGuard", Order = 4)]
    public async Task ResetWriteCountsAfterAsync()
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
