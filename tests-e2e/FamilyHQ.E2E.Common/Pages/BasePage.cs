using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public abstract class BasePage
{
    protected readonly IPage Page;
    public abstract string PageUrl { get; }

    protected BasePage(IPage page)
    {
        Page = page;
    }

    public async Task NavigateAsync()
    {
        await Page.GotoAsync(PageUrl);
    }

    public async Task WaitForLoadAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
