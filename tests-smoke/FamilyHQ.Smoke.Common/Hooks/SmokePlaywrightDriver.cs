using FamilyHQ.Smoke.Common.Configuration;
using FamilyHQ.Smoke.Common.Helpers;
using Microsoft.Playwright;

namespace FamilyHQ.Smoke.Common.Hooks;

/// <summary>
/// Launches the kiosk browser for one smoke scenario, already signed in to preprod.
/// <para>
/// Two things make this different from the E2E driver it is modelled on:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Sign-in is injected, not driven.</b> The session JWT comes from preprod's
///     <c>issue-token</c> endpoint and is written straight into the kiosk's localStorage before the
///     first navigation. Google's interactive consent screen is not automatable from CI, which is the
///     reason those endpoints exist (FHQ-139). The token is written through an init script and never
///     leaves this method.
///   </description></item>
///   <item><description>
///     <b>Certificate errors are not ignored by default.</b> E2E runs against a dev certificate and
///     waves them through; preprod is a real environment behind an internal CA, and the answer to
///     "the chain does not validate" is to trust that CA on the agent. <c>Smoke__AllowUntrustedCertificate</c>
///     exists for an exploratory run from a machine that cannot, and defaults to off.
///   </description></item>
/// </list>
/// </summary>
public sealed class SmokePlaywrightDriver : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    /// <summary>Console errors and unhandled page errors seen since the page opened.</summary>
    public IReadOnlyList<string> PageProblems => _pageProblems;

    private readonly List<string> _pageProblems = [];

    public IPage? Page { get; private set; }

    /// <summary>
    /// Opens a browser pinned to the family's zone, seeds the session JWT and the scenario's
    /// correlation id, and returns the page. Nothing is navigated yet — the page object owns that.
    /// </summary>
    public async Task<IPage> InitialiseAsync(
        SmokeConfiguration configuration, string sessionJwt, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = configuration.Headless,
            Timeout = configuration.DefaultTimeoutMs
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = configuration.BaseUrl,
            IgnoreHTTPSErrors = configuration.AllowUntrustedCertificate,
            // The kiosk renders every instant in the BROWSER's zone. Pinning it to the family's zone
            // is what makes "the same wall-clock time" a meaningful assertion rather than an accident
            // of where the agent happens to be.
            TimezoneId = FamilyClock.TimeZoneId
        });

        _context.SetDefaultTimeout(configuration.DefaultTimeoutMs);

        Page = await _context.NewPageAsync();

        Page.Console += OnConsoleMessage;
        Page.PageError += OnPageError;

        // Runs before any page script on every navigation in this context, so the very first load is
        // already authenticated and already correlated. JSON-encoded so a value can never break out of
        // the string literal.
        await Page.AddInitScriptAsync(
            $"""
             localStorage.setItem('familyhq_auth_token', {ToJsonString(sessionJwt)});
             localStorage.setItem('familyhq_session_correlation_id', {ToJsonString(correlationId)});
             """);

        return Page;
    }

    public async ValueTask DisposeAsync()
    {
        if (Page is not null)
        {
            Page.Console -= OnConsoleMessage;
            Page.PageError -= OnPageError;
            await Page.CloseAsync();
        }

        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    private static string ToJsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private void OnConsoleMessage(object? sender, IConsoleMessage message)
    {
        if (string.Equals(message.Type, "error", StringComparison.Ordinal))
        {
            _pageProblems.Add($"console.error: {SecretGuard.Scrub(message.Text)}");
        }
    }

    private void OnPageError(object? sender, string error) =>
        _pageProblems.Add($"pageerror: {SecretGuard.Scrub(error)}");
}
