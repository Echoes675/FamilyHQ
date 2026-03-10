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
    private ILocator LoadingSpinner => Page.Locator(".spinner-border.text-primary");
    private ILocator MonthTable => Page.Locator("table.month-table");
    private ILocator EventCapsules => Page.Locator(".event-capsule");
    private ILocator LoginSimulatorBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Login to Google (Simulator)" });
    private ILocator NextMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Next >" });
    private ILocator AddEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Add Event" });
    
    // Modal Locators
    private ILocator EventTitleInput => Page.GetByPlaceholder("e.g. Doctor Appointment");
    private ILocator SaveEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
    private ILocator DeleteEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
    private ILocator EventModal => Page.Locator(".modal-content");

    // Actions
    public async Task WaitForCalendarToLoadAsync()
    {
        // Wait for the loading spinner to disappear
        if (await LoadingSpinner.IsVisibleAsync())
        {
            await LoadingSpinner.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        }
        
        // Ensure the month table is visible
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SimulateLoginAsync(string userName = "Test Family Member")
    {
        await LoginSimulatorBtn.ClickAsync();
        
        var loginModal = Page.Locator(".login-modal-content");
        await loginModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        var inputList = loginModal.GetByPlaceholder("e.g. Test Family Member");
        await inputList.FillAsync(userName);
        
        await Page.GetByRole(AriaRole.Button, new() { Name = "Simulate OAuth & Proceed" }).ClickAsync();
        
        await WaitForCalendarToLoadAsync();
    }

    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        await EventTitleInput.FillAsync(title);
        await SaveEventBtn.ClickAsync();
        
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarToLoadAsync();
    }

    public async Task UpdateEventAsync(string oldTitle, string newTitle)
    {
        await Page.GetByText(oldTitle).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        await EventTitleInput.FillAsync(newTitle);
        await SaveEventBtn.ClickAsync();
        
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarToLoadAsync();
    }

    public async Task DeleteEventAsync(string title)
    {
        await Page.GetByText(title).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        await DeleteEventBtn.ClickAsync();
        
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarToLoadAsync();
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
