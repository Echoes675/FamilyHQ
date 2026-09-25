using Microsoft.Playwright;

namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// Base for the smoke suite's page objects.
/// <para>
/// This is a deliberate duplicate of the E2E suite's equivalent rather than a shared library. The two
/// suites answer different questions of different environments and will drift apart on purpose — the
/// smoke suite must be free to change how it drives the kiosk without a ripple through 167 E2E
/// scenarios, and vice versa. Coupling them would make every preprod quirk an E2E maintenance event.
/// </para>
/// </summary>
public abstract class SmokeBasePage(IPage page)
{
    protected IPage Page { get; } = page;

    /// <summary>The absolute URL this page object navigates to.</summary>
    public abstract string PageUrl { get; }

    public Task NavigateAsync() => Page.GotoAsync(PageUrl);
}
