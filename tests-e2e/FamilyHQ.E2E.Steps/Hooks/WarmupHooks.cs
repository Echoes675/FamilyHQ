using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps.Hooks;

/// <summary>
/// Local-only warm-up (FHQ-150). When dev-stack sets <c>DEVSTACK_E2E_WARMUP=1</c>, load the WebUi
/// once before any scenario so a freshly-booted local stack is warm. Otherwise the first scenario
/// races a cold WASM boot + a still-JITing backend, intermittently blowing the 30s element waits.
/// Not run in CI: the deployed app is already warm, so only the dev-stack e2e verb sets the flag.
/// A single pre-scenario browser also stays clear of the parallel-Playwright transport crash.
/// </summary>
[Binding]
public class WarmupHooks
{
    [BeforeTestRun]
    public static async Task WarmUpAsync()
    {
        if (Environment.GetEnvironmentVariable("DEVSTACK_E2E_WARMUP") != "1")
        {
            return;
        }

        var config = ConfigurationLoader.Load();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = config.Headless });
        var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, BaseURL = config.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(config.BaseUrl + "/", new() { Timeout = 60000 });
            await page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" })
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
        }
        catch
        {
            // Best-effort: a warm-up failure must never fail the run — the scenarios do the real asserting.
        }
    }
}
