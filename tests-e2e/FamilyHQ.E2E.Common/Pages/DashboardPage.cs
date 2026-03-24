using System.Text.RegularExpressions;
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

    public async Task LoginAsync(string userName)
    {
        // Follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        await LoginBtn.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 15000 });
        await Page.Locator("select#selectedUserId").SelectOptionAsync(new SelectOptionValue { Label = userName });
        await Page.Locator("button[type='submit']").ClickAsync();
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

    public async Task ChangeEventCalendarAsync(string eventName, string targetCalendarName)
    {
        await Page.GetByText(eventName).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        // Activate the target calendar chip if it is not already active.
        var targetChip = EventModal.Locator($".chip >> text={targetCalendarName}");
        var targetClasses = await targetChip.GetAttributeAsync("class") ?? "";
        if (!targetClasses.Contains("chip-active"))
            await targetChip.ClickAsync();

        // Deactivate all active chips that are not the target calendar.
        // Iterate in reverse to avoid index drift as chips change state.
        var activeChips = EventModal.Locator(".chip-active");
        var activeCount = await activeChips.CountAsync();
        for (int i = activeCount - 1; i >= 0; i--)
        {
            var chip = activeChips.Nth(i);
            var text = await chip.InnerTextAsync();
            if (text.Contains(targetCalendarName)) continue;

            var removeBtn = chip.Locator(".chip-remove");
            if (await removeBtn.CountAsync() > 0)
                await removeBtn.ClickAsync();
        }

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

    public async Task OpenEventForEditingAsync(string eventName)
    {
        await Page.GetByText(eventName).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Activates the chip for <paramref name="calendarName"/> in the event modal chip selector,
    /// saves the event and waits for the calendar to reload.
    /// </summary>
    public async Task AddCalendarChipToEventAsync(string calendarName)
    {
        var chip = EventModal.Locator($".chip >> text={calendarName}");
        await chip.ClickAsync();

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Removes the chip for <paramref name="calendarName"/> from the event modal chip selector,
    /// saves the event and waits for the calendar to reload.
    /// </summary>
    public async Task RemoveCalendarChipFromEventAsync(string calendarName)
    {
        var removeBtn = EventModal.Locator($"[aria-label='Remove {calendarName}']");
        await removeBtn.ClickAsync();

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Creates an event in two named calendars by filling the title and activating both chips.
    /// </summary>
    public async Task CreateEventInCalendarsAsync(string title, string calendarName1, string calendarName2)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });

        await EventTitleInput.FillAsync(title);

        // Activate both chips — the primary calendar chip may already be active;
        // clicking an already-active chip toggles it off, so we check state first.
        // After each click, wait for Blazor to reflect the active state in the DOM
        // before proceeding, to avoid a race where Save fires before state is committed.
        //
        // Use HasText filter to target the outer .chip <div>, not the inner <span class="chip-name">.
        // The ">>" chain would resolve to the inner span which does not carry chip-active.
        var chip1 = EventModal.Locator(".chip").Filter(new() { HasText = calendarName1 });
        var chip1Classes = await chip1.GetAttributeAsync("class") ?? "";
        if (!chip1Classes.Contains("chip-active"))
        {
            await chip1.ClickAsync();
            await Assertions.Expect(chip1).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var chip2 = EventModal.Locator(".chip").Filter(new() { HasText = calendarName2 });
        var chip2Classes = await chip2.GetAttributeAsync("class") ?? "";
        if (!chip2Classes.Contains("chip-active"))
        {
            await chip2.ClickAsync();
            await Assertions.Expect(chip2).ToHaveClassAsync(new Regex("chip-active"), new() { Timeout = 5000 });
        }

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    /// <summary>
    /// Returns whether any event capsule for <paramref name="eventName"/> is rendered
    /// in the background colour of <paramref name="calendarName"/>.
    /// </summary>
    public async Task<bool> IsEventDisplayedInCalendarColourAsync(string eventName, string calendarColor)
    {
        var count = await EventCapsules.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var capsule = EventCapsules.Nth(i);
            var text = await capsule.InnerTextAsync();
            if (!text.Contains(eventName)) continue;

            var style = await capsule.GetAttributeAsync("style") ?? "";
            if (style.Contains(calendarColor, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if no event capsule for <paramref name="eventName"/> carries
    /// the background colour associated with <paramref name="calendarName"/>.
    /// </summary>
    public async Task<bool> NoEventCapsuleWithCalendarColourAsync(string eventName, string calendarName, string calendarColor)
    {
        var count = await EventCapsules.CountAsync();
        for (int i = 0; i < count; i++)
        {
            var capsule = EventCapsules.Nth(i);
            var text = await capsule.InnerTextAsync();
            if (!text.Contains(eventName)) continue;

            var style = await capsule.GetAttributeAsync("style") ?? "";
            if (style.Contains(calendarColor, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true when the only active chip in the event modal has no remove button visible,
    /// which is the "last chip protected" invariant.
    /// </summary>
    public async Task<bool> LastActiveChipHasNoRemoveButtonAsync()
    {
        var activeChips = EventModal.Locator(".chip-active");
        var activeCount = await activeChips.CountAsync();
        if (activeCount != 1) return false;

        var removeBtn = activeChips.First.Locator(".chip-remove");
        return await removeBtn.CountAsync() == 0;
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

    /// <summary>
    /// Returns the background-color hex value declared for <paramref name="calendarName"/>
    /// in the chip selector within the open event modal.
    /// The colour is read from the <c>--chip-color</c> CSS variable on the chip element.
    /// </summary>
    public async Task<string> GetChipColourForCalendarAsync(string calendarName)
    {
        var chip = EventModal.Locator($".chip >> text={calendarName}");
        var style = await chip.GetAttributeAsync("style") ?? "";
        // style is e.g. "--chip-color: #ea4335"
        var idx = style.IndexOf("#", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var raw = style[idx..].Split(';', ' ')[0].Trim();
            return raw;
        }
        return string.Empty;
    }
}
