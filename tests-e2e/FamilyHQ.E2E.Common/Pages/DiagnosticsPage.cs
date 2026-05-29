using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

/// <summary>
/// Page object for the /diagnostics page. Exposes the data-testid hooks declared
/// on the Diagnostics.razor template so step definitions never depend on
/// markup details.
/// </summary>
public class DiagnosticsPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/diagnostics";

    public DiagnosticsPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    public ILocator RefreshBtn          => Page.GetByTestId("diagnostics-refresh-btn");
    public ILocator LoadError           => Page.GetByTestId("diagnostics-load-error");
    public ILocator ConnectionLoading   => Page.GetByTestId("diagnostics-connection-loading");
    public ILocator StatusBadge         => Page.GetByTestId("diagnostics-status-badge");
    public ILocator LastError           => Page.GetByTestId("diagnostics-last-error");
    public ILocator Since               => Page.GetByTestId("diagnostics-since");
    public ILocator ReconnectBtn        => Page.GetByTestId("diagnostics-reconnect-btn");
    public ILocator CalendarsTable      => Page.GetByTestId("diagnostics-calendars-table");
    public ILocator FailuresEmptyState  => Page.GetByTestId("diagnostics-failures-empty");
    public ILocator FailuresTable       => Page.GetByTestId("diagnostics-failures-table");
    public ILocator RunsEmptyState      => Page.GetByTestId("diagnostics-runs-empty");
    public ILocator RunsTable           => Page.GetByTestId("diagnostics-runs-table");

    /// <summary>
    /// Navigates to /diagnostics and waits for the connection status section to render.
    /// </summary>
    public async Task GotoAsync()
    {
        // FHQ-28: wait for network-idle so Blazor WASM bootstrap + SignalR connect both complete before the locator wait begins.
        await Page.GotoAsync(PageUrl, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await WaitForLoadedAsync();
    }

    /// <summary>
    /// Waits until the status badge has rendered (either Active or Needs Reauth)
    /// or the load-error banner is visible.
    /// </summary>
    public async Task WaitForLoadedAsync()
    {
        await Page.Locator(
            "[data-testid='diagnostics-status-badge'], [data-testid='diagnostics-load-error']")
            .First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task<string> GetStatusBadgeTextAsync()
    {
        await StatusBadge.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return (await StatusBadge.InnerTextAsync()).Trim();
    }

    public async Task<string> GetLastErrorTextAsync()
    {
        await LastError.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return (await LastError.InnerTextAsync()).Trim();
    }

    public async Task<int> GetFailureRowCountAsync()
    {
        // If the empty-state placeholder is visible, the table is absent.
        if (await FailuresEmptyState.IsVisibleAsync())
            return 0;

        await FailuresTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return await FailuresTable.Locator("tbody tr").CountAsync();
    }

    public Task<bool> IsEmptyStateVisibleAsync() => FailuresEmptyState.IsVisibleAsync();

    public async Task<int> GetFailedRunRowCountAsync()
    {
        if (await RunsEmptyState.IsVisibleAsync())
            return 0;

        await RunsTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return await RunsTable.Locator("tbody tr").CountAsync();
    }

    public async Task ClickReconnectAsync()
    {
        await ReconnectBtn.ClickAsync();
    }
}
