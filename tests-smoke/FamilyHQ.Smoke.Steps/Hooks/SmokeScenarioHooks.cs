using FamilyHQ.Smoke.Common.Correlation;
using FamilyHQ.Smoke.Common.Hooks;
using FamilyHQ.Smoke.Common.Pages;
using FamilyHQ.Smoke.Data.Api;
using FamilyHQ.Smoke.Steps.Preflight;
using Reqnroll;
using Xunit.Abstractions;

namespace FamilyHQ.Smoke.Steps.Hooks;

/// <summary>
/// Everything that happens around a smoke scenario: its correlation identity, its clients, its kiosk, the
/// environment gate, and the read-only forensics captured when it fails.
/// </summary>
[Binding]
public sealed class SmokeScenarioHooks(
    ScenarioContext scenarioContext, FeatureContext featureContext, ITestOutputHelper output)
{
    /// <summary>
    /// Every tag that applies to this scenario, its feature's included.
    /// <para>
    /// <see cref="ScenarioInfo.Tags"/> carries only the tags written on the scenario itself, and both tags
    /// this suite uses are declared once at feature level. Reading the scenario's tags alone made
    /// <c>@preflight</c> invisible — so the health gate fired on the preflight feature and replaced seven
    /// specific diagnoses with seven copies of "the environment is unhealthy" — and made <c>@kiosk</c>
    /// invisible too, so no browser would ever have started.
    /// </para>
    /// </summary>
    private IEnumerable<string> ApplicableTags =>
        scenarioContext.ScenarioInfo.Tags.Concat(featureContext.FeatureInfo.Tags);

    private bool HasTag(string tag) => ApplicableTags.Contains(tag, StringComparer.Ordinal);

    /// <summary>
    /// Obtains this scenario's correlation identity and clients, running preflight on the way if this is
    /// the first scenario of the run.
    /// </summary>
    [BeforeScenario(Order = 0)]
    public async Task InitialiseAsync()
    {
        var environment = await SmokePreflight.EnsureAsync();
        var correlation = SmokeCorrelation.New();

        // The one line that makes a failure traceable. It carries the correlation id and the short id and
        // nothing else — never the JWT, never the Google token, never the shared secret.
        output.WriteLine(
            $"Smoke scenario '{scenarioContext.ScenarioInfo.Title}' correlation={correlation.Id} "
            + $"short={correlation.ShortId}");

        // The API client is built only when there is a token to build it with. A preflight scenario must be
        // able to run and report on an environment that cannot even mint one.
        var state = new SmokeScenarioState
        {
            Environment = environment,
            Correlation = correlation,
            Api = environment.SessionJwtOrNull is { } jwt
                ? new PreprodApiClient(environment.Configuration, jwt, correlation.Id)
                : null
        };

        scenarioContext.Set(state);
    }

    /// <summary>
    /// Refuses to run a core scenario against an environment that failed a preflight check.
    /// <para>
    /// This is FHQ-141 principle 1 made mechanical. Without it, a run against an environment whose shared
    /// calendar flag is missing could still go green — the scenarios would be asserting something, just not
    /// the thing anyone believed they were asserting. The preflight feature itself is exempt: its job is to
    /// name what is wrong.
    /// </para>
    /// </summary>
    [BeforeScenario(Order = 10)]
    public void GateOnEnvironmentHealth()
    {
        if (HasTag(SmokeTags.Preflight))
        {
            return;
        }

        var report = scenarioContext.Get<SmokeScenarioState>().Environment.Report;
        if (report.IsHealthy)
        {
            return;
        }

        throw new InvalidOperationException(
            "Refusing to run: preprod failed one or more preflight checks, and a green run against a bad "
            + "environment is worse than a red one. Fix the environment (nothing here will), then re-run."
            + Environment.NewLine + report.FailureSummary);
    }

    /// <summary>Starts the kiosk browser, already signed in, for scenarios that drive the UI.</summary>
    [BeforeScenario(Order = 20)]
    public async Task StartKioskAsync()
    {
        if (!HasTag(SmokeTags.Kiosk))
        {
            return;
        }

        var state = scenarioContext.Get<SmokeScenarioState>();
        var driver = new SmokePlaywrightDriver();

        var page = await driver.InitialiseAsync(
            state.Environment.Configuration, state.Environment.SessionJwt, state.Correlation.Id);

        state.Driver = driver;
        state.Dashboard = new SmokeDashboardPage(page, state.Environment.Configuration);
    }

    /// <summary>
    /// On failure, gathers what a post-mortem needs — and nothing else.
    /// <para>
    /// Strictly read-only, per FHQ-141 principle 2: a screenshot, the correlation id, the browser's console
    /// errors, and what Google currently holds for this scenario's events. Nothing is created, deleted,
    /// re-synced or retried. The events this scenario made are deliberately left in place for the same
    /// reason, which is why every series it creates is bounded.
    /// </para>
    /// </summary>
    [AfterScenario(Order = 0)]
    public async Task CaptureForensicsOnFailureAsync()
    {
        if (scenarioContext.TestError is null || !scenarioContext.TryGetValue(out SmokeScenarioState state))
        {
            return;
        }

        output.WriteLine(
            $"Smoke scenario FAILED. correlation={state.Correlation.Id} short={state.Correlation.ShortId} "
            + "— grep Seq for that correlation id, and look for the short id in the retained events on the "
            + "smoke account's calendars.");

        foreach (var problem in state.Driver?.PageProblems ?? [])
        {
            output.WriteLine($"  kiosk console: {problem}");
        }

        await TryCaptureScreenshotAsync(state);
    }

    [AfterScenario(Order = 10)]
    public async Task DisposeAsync()
    {
        if (!scenarioContext.TryGetValue(out SmokeScenarioState state))
        {
            return;
        }

        if (state.Driver is not null)
        {
            await state.Driver.DisposeAsync();
        }

        state.Api?.Dispose();
    }

    /// <summary>Releases the run-scoped Google client once every scenario has finished.</summary>
    [AfterTestRun]
    public static void ReleaseRunEnvironment() => SmokePreflight.Release();

    private async Task TryCaptureScreenshotAsync(SmokeScenarioState state)
    {
        var page = state.Driver?.Page;
        if (page is null)
        {
            return;
        }

        try
        {
            var directory = Path.Combine("TestResults", "smoke-artifacts");
            Directory.CreateDirectory(directory);

            var safeTitle = string.Concat(
                scenarioContext.ScenarioInfo.Title.Select(
                    character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

            var path = Path.Combine(directory, $"{safeTitle}-{state.Correlation.ShortId}.png");
            await page.ScreenshotAsync(new Microsoft.Playwright.PageScreenshotOptions { Path = path });
            output.WriteLine($"  kiosk screenshot: {path}");
        }
        catch (Exception ex)
        {
            // A capture problem must never replace the real failure, and there is nothing to retry.
            output.WriteLine($"  kiosk screenshot could not be captured: {ex.Message}");
        }
    }
}
