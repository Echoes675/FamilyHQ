using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

/// <summary>
/// Lightweight page-object helper for the application footer rendered in
/// <c>MainLayout.razor</c>. Exposes locators for the version span and the
/// update banner shown by <c>VersionService</c> when a deployment changes
/// the SemVer core reported by <c>/api/health</c>.
/// </summary>
public class FooterComponent
{
    private readonly IPage _page;

    public FooterComponent(IPage page)
    {
        _page = page;
    }

    public ILocator VersionText => _page.GetByTestId("app-version");

    public ILocator UpdateBanner => _page.GetByTestId("update-banner");

    public async Task<string> GetVersionAsync()
    {
        return (await VersionText.TextContentAsync()) ?? string.Empty;
    }
}
