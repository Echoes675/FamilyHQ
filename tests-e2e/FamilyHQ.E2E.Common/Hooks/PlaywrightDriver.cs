using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Hooks;

public class PlaywrightDriver : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _browserContext;

    public IPage? Page { get; private set; }

    public async Task<IPage> InitializeAsync(Configuration.TestConfiguration config)
    {
        _playwright = await Playwright.CreateAsync();
        
        var options = new BrowserTypeLaunchOptions
        {
            Headless = config.Headless,
            Timeout = config.DefaultTimeoutMs
        };

        _browser = await _playwright.Chromium.LaunchAsync(options);
        
        var contextOptions = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true, // Important for local development with self-signed certs
            BaseURL = config.BaseUrl,
            // Pin the browser's zone instead of inheriting the CI host's (UTC). Blazor WASM derives
            // "today" and renders every DateTimeOffset in the BROWSER's zone, so an unpinned browser
            // put the app on a different calendar day from the seed for one hour a night during BST
            // (intermittent-issues #11). Every test-side date calculation resolves through the same
            // BrowserClock, so the two cannot drift apart.
            TimezoneId = Helpers.BrowserClock.TimeZoneId
        };
        
        _browserContext = await _browser.NewContextAsync(contextOptions);
        Page = await _browserContext.NewPageAsync();
        
        return Page;
    }

    public async ValueTask DisposeAsync()
    {
        if (Page != null) await Page.CloseAsync();
        if (_browserContext != null) await _browserContext.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}
