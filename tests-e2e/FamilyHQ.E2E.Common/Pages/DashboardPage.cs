using FamilyHQ.E2E.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.E2E.Common.Pages;

public class DashboardPage : BasePage
{
    private readonly TestConfiguration _config;
    public override string PageUrl => _config.BaseUrl + "/";

    public DashboardPage(IPage page) : base(page)
    {
        _config = ConfigurationLoader.Load();
    }

    // Locators
    public ILocator MonthTable => Page.Locator("table.month-table");
    public ILocator EventCapsules => Page.Locator(".event-capsule");
    public ILocator LoginBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });
    public ILocator SignOutBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Sign Out" });
    public ILocator UserInfo => Page.GetByText("Signed in as:");
    private ILocator NextMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Next >" });
    private ILocator AddEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Add Event" });

    // Modal Locators
    private ILocator EventTitleInput => Page.GetByPlaceholder("e.g. Doctor Appointment");
    private ILocator SaveEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
    private ILocator DeleteEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
    private ILocator EventModal => Page.Locator(".modal-content");

    // Actions

    /// <summary>
    /// Navigates to the dashboard and waits for the events API response before returning,
    /// ensuring calendar data is rendered. Listener is set up before navigation to avoid
    /// missing fast responses.
    /// </summary>
    public async Task NavigateAndWaitAsync()
    {
        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });
        await NavigateAsync();
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Waits for the next events API response. Call this only when you know a response
    /// is already in-flight (e.g., immediately after a UI action that triggers a reload).
    /// </summary>
    public async Task WaitForCalendarToLoadAsync()
    {
        await Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SimulateLoginAsync(string userName = "Test Family Member")
    {
        await LoginBtn.ClickAsync();

        var loginModal = Page.Locator(".login-modal-content");
        await loginModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var inputList = loginModal.GetByPlaceholder("e.g. Test Family Member");
        await inputList.FillAsync(userName);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Simulate OAuth & Proceed" }).ClickAsync();

        await WaitForCalendarToLoadAsync();
    }

    public async Task SignOutAsync()
    {
        // Check if sign-out button exists (user is authenticated)
        if (await SignOutBtn.CountAsync() > 0)
        {
            await SignOutBtn.ClickAsync();
            // Wait for the login button to appear, confirming sign-out is complete
            await LoginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        }
    }

    public async Task<bool> IsSignedInAsync()
    {
        // Check if sign-out button or user info is visible
        return await SignOutBtn.CountAsync() > 0 || await UserInfo.CountAsync() > 0;
    }

    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(title);

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task UpdateEventAsync(string oldTitle, string newTitle)
    {
        await Page.GetByText(oldTitle).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(newTitle);

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task DeleteEventAsync(string title)
    {
        await Page.GetByText(title).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await DeleteEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task ClickEventAsync(string eventName)
    {
        await Page.GetByText(eventName).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task<string> GetEventDetailsAsync()
    {
        return await EventTitleInput.InputValueAsync();
    }

    public async Task NavigateToNextMonthAsync()
    {
        var nextMonth = DateTime.Today.AddMonths(1);
        var expectedMonthText = nextMonth.ToString("MMMM yyyy"); // e.g. "April 2026"

        await NextMonthBtn.ClickAsync();

        // Wait for the month header button to display the next month, confirming the
        // navigation has been processed and the UI has re-rendered.
        var monthHeaderBtn = Page.GetByRole(AriaRole.Button, new() { Name = expectedMonthText });
        await monthHeaderBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // Give the loading cycle time to complete. The table may still be showing
        // stale data if Blazor batched the renders, so use an explicit small wait
        // to let the HTTP response return and the final render complete.
        await Page.WaitForTimeoutAsync(3000);

        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    // Assertions / State Checks
    public async Task<IReadOnlyList<string>> GetVisibleEventsAsync()
    {
        var count = await EventCapsules.CountAsync();
        var titles = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var text = await EventCapsules.Nth(i).InnerTextAsync();
            titles.Add(text);
        }

        return titles;
    }
}
