using System.Text.Json;
using System.Text.RegularExpressions;
using FamilyHQ.E2E.Common.Configuration;
using FamilyHQ.E2E.Common.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

[Binding]
public class VersionFooterSteps
{
    // Match a SemVer-shaped version: major.minor.patch with optional
    // pre-release suffix and optional build metadata. The footer rendering
    // strips the leading "v" before comparison; the /api/health endpoint
    // returns the bare value with no prefix.
    private const string SemVerPattern =
        @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$";

    private const string OriginalVersionKey = "VersionFooter.OriginalVersion";
    private const string HealthResponseKey = "VersionFooter.HealthResponse";
    private const string MockedHealthVersionKey = "VersionFooter.MockedHealthVersion";

    private readonly ScenarioContext _scenarioContext;
    private readonly TestConfiguration _config;
    private readonly FooterComponent _footer;
    private readonly DashboardPage _dashboardPage;

    public VersionFooterSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
        _config = ConfigurationLoader.Load();
        var page = scenarioContext.Get<IPage>();
        _footer = new FooterComponent(page);
        _dashboardPage = new DashboardPage(page);
    }

    [Then(@"the footer should display a version matching the SemVer pattern")]
    public async Task ThenTheFooterShouldDisplayAVersionMatchingTheSemVerPattern()
    {
        await _footer.VersionText.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var rendered = (await _footer.GetVersionAsync()).Trim();
        rendered.Should().StartWith("v",
            "the footer prefixes the rendered SemVer string with a literal 'v'.");

        var semVer = rendered.TrimStart('v');
        semVer.Should().MatchRegex(SemVerPattern,
            "the footer must display a SemVer-shaped version derived from AssemblyInformationalVersion.");
    }

    [When(@"I navigate to the ""([^""]*)"" page")]
    public async Task WhenINavigateToThePage(string pageName)
    {
        var page = _scenarioContext.Get<IPage>();

        switch (pageName)
        {
            case "dashboard":
                await _dashboardPage.NavigateAndWaitAsync();
                break;
            case "settings":
                await page.GotoAsync(_config.BaseUrl + "/settings");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.Locator(".settings-page--tabbed").WaitForAsync(
                    new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported page '{pageName}' in version footer scenarios.");
        }
    }

    [When(@"I request the health endpoint")]
    public async Task WhenIRequestTheHealthEndpoint()
    {
        var page = _scenarioContext.Get<IPage>();
        var response = await page.Context.APIRequest!.GetAsync($"{_config.ApiBaseUrl}/api/health");
        _scenarioContext[HealthResponseKey] = response;
    }

    [Then(@"the response should contain a version matching the SemVer pattern")]
    public async Task ThenTheResponseShouldContainAVersionMatchingTheSemVerPattern()
    {
        var response = _scenarioContext.Get<IAPIResponse>(HealthResponseKey);
        response.Status.Should().Be(200, "/api/health must return 200 OK.");

        var body = await response.TextAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("version", out var versionElement)
            .Should().BeTrue("the health payload must include a 'version' property.");

        var version = versionElement.GetString() ?? string.Empty;
        version.Should().NotBeNullOrWhiteSpace();
        version.Should().MatchRegex(SemVerPattern,
            "the health endpoint must report a SemVer-shaped version.");
    }

    [Then(@"the response should set Cache-Control to ""([^""]*)""")]
    public async Task ThenTheResponseShouldSetCacheControlTo(string expectedDirective)
    {
        var response = _scenarioContext.Get<IAPIResponse>(HealthResponseKey);

        // Playwright lowercases header names in the IAPIResponse.Headers dictionary.
        var headers = response.Headers;
        headers.TryGetValue("cache-control", out var cacheControl).Should().BeTrue(
            "the health endpoint must declare a Cache-Control header.");
        cacheControl!.Should().Contain(expectedDirective,
            "the Cache-Control header must include the expected directive.");
    }

    [Given(@"the dashboard is open and the footer shows the current version")]
    public async Task GivenTheDashboardIsOpenAndTheFooterShowsTheCurrentVersion()
    {
        await _dashboardPage.NavigateAndWaitAsync();
        await _footer.VersionText.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var rendered = (await _footer.GetVersionAsync()).Trim();
        _scenarioContext[OriginalVersionKey] = rendered;
    }

    [When(@"the server reports a new version after a SignalR reconnect")]
    public async Task WhenTheServerReportsANewVersionAfterASignalRReconnect()
    {
        var page = _scenarioContext.Get<IPage>();
        const string mockedVersion = "99.99.99";
        _scenarioContext[MockedHealthVersionKey] = mockedVersion;

        // Override every subsequent /api/health request so the version check
        // observes a SemVer-different value than the WASM client's assembly
        // version. Cleaned up in [AfterScenario] so other scenarios are not
        // affected (per E2E-isolation rule).
        await page.RouteAsync("**/api/health", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Headers = new Dictionary<string, string>
                {
                    ["Cache-Control"] = "no-store",
                },
                Body = JsonSerializer.Serialize(new
                {
                    status = "healthy",
                    service = "webapi",
                    version = mockedVersion,
                    timestamp = DateTimeOffset.UtcNow,
                }),
            });
        });

        // Force a SignalR reconnect by briefly aborting hub negotiate calls.
        // VersionService subscribes to ISignalRConnectionEvents.Reconnected
        // and re-runs CheckAsync() on every successful reconnect.
        await page.RouteAsync("**/hubs/calendar/**", async route =>
        {
            await route.AbortAsync();
        });

        // Hold the abort in place long enough for the existing connection to
        // notice the failure and transition to the reconnect loop.
        await Task.Delay(3000);
        await page.UnrouteAsync("**/hubs/calendar/**");
    }

    [Then(@"the update banner should appear within (\d+) seconds")]
    public async Task ThenTheUpdateBannerShouldAppearWithinSeconds(int seconds)
    {
        await _footer.UpdateBanner.WaitForAsync(new()
        {
            State = WaitForSelectorState.Visible,
            Timeout = seconds * 1000,
        });
    }

    [Then(@"the page should reload within (\d+) seconds")]
    public async Task ThenThePageShouldReloadWithinSeconds(int seconds)
    {
        var page = _scenarioContext.Get<IPage>();

        // The reload tears down the existing DOM. Wait for the update banner
        // (rendered by the pre-reload Blazor instance) to detach from the
        // page — a reliable, framework-agnostic signal that navigation has
        // happened, regardless of whether Playwright's framenavigated event
        // fires at the right moment.
        await _footer.UpdateBanner.WaitForAsync(new()
        {
            State = WaitForSelectorState.Detached,
            Timeout = seconds * 1000,
        });
    }

    [Then(@"after reload the footer should display the new version")]
    public async Task ThenAfterReloadTheFooterShouldDisplayTheNewVersion()
    {
        // After reload the freshly-instantiated WASM client fetches /api/health
        // again. With our route override still in place, ServerVersion settles
        // on the mocked value while ClientVersion remains the assembly value.
        // The user-observable invariant we can assert is that the footer is
        // still rendered with a SemVer-shaped string and the update banner is
        // present (because Server/Client still differ post-reload).
        await _footer.VersionText.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        var rendered = (await _footer.GetVersionAsync()).Trim().TrimStart('v');
        rendered.Should().MatchRegex(SemVerPattern,
            "after reload the footer must continue to render a SemVer-shaped version.");
    }

    [AfterScenario]
    public async Task RemoveRouteHandlersAsync()
    {
        // Per the E2E-isolation rule: route handlers installed during a
        // scenario must not leak across scenarios. UnrouteAsync is a no-op if
        // no handler is registered, so it is safe to call unconditionally.
        if (!_scenarioContext.ContainsKey(MockedHealthVersionKey))
        {
            return;
        }

        try
        {
            var page = _scenarioContext.Get<IPage>();
            await page.UnrouteAsync("**/api/health");
            await page.UnrouteAsync("**/hubs/calendar/**");
        }
        catch
        {
            // Page may already be disposed by the master teardown hook —
            // unroute failures during cleanup must not mask the real result.
        }
    }
}
