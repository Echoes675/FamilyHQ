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
            BaseURL = config.BaseUrl
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
