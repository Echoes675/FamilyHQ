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
    public ILocator DayViewContainer => Page.Locator(".day-view-container");
    public ILocator AgendaViewContainer => Page.Locator(".agenda-view-container");
    public ILocator MonthTab => Page.GetByTestId("month-tab");
    public ILocator DayTab => Page.GetByTestId("day-tab");
    public ILocator AgendaTab => Page.GetByTestId("agenda-tab");
    public ILocator EventCapsules => Page.Locator(".event-capsule");
    public ILocator CurrentTimeLine => Page.Locator(".current-time-line");
    public ILocator LoginBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Login to Google" });
    public ILocator SignOutBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Sign Out" });
    public ILocator UserInfo => Page.GetByText("Signed in as:");
    private ILocator NextMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Next ›" });
    private ILocator PrevMonthBtn => Page.GetByRole(AriaRole.Button, new() { Name = "‹ Prev" });
    private ILocator AddEventBtn => Page.GetByTestId("add-event-btn");

    // Modal Locators
    private ILocator EventTitleInput => Page.GetByPlaceholder("e.g. Doctor Appointment");
    private ILocator SaveEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Save" });
    private ILocator DeleteEventBtn => Page.GetByRole(AriaRole.Button, new() { Name = "Delete" });
    private ILocator EventModal => Page.Locator(".modal-content");
    private ILocator DayPickerBtn => Page.GetByTestId("day-picker-btn");
    private ILocator DayPickerInput => Page.GetByTestId("day-picker-input");
    private ILocator DayPickerGoBtn => Page.GetByTestId("day-picker-go-btn");
    private ILocator DayPickerModal => Page.Locator(".modal-backdrop").Filter(new() { HasText = "Select Date" });

    // Weather Locators
    public ILocator WeatherStrip => Page.Locator(".weather-strip");
    public ILocator WeatherStripTemp => Page.Locator(".weather-strip__temp");
    public ILocator WeatherStripCondition => Page.Locator(".weather-strip__condition");
    public ILocator WeatherStripForecastDays => Page.Locator(".weather-strip__forecast-day");
    public ILocator WeatherOverlay => Page.Locator("#weather-overlay");

    public ILocator AgendaWeatherForDate(string dateKey) =>
        Page.GetByTestId($"agenda-day-label-{dateKey}").Locator(".agenda-weather");
    public ILocator AgendaWeatherTemps(string dateKey) =>
        Page.GetByTestId($"agenda-day-label-{dateKey}").Locator(".agenda-weather__temps");
    public ILocator DayHourTemps => Page.Locator(".day-hour-temp");

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
        
        // Wait for either view to be ready
        await WaitForCalendarVisibleAsync();
    }

    private async Task WaitForCalendarVisibleAsync()
    {
        // Wait for either the month table or day view container to be visible.
        // This is safe to call at any point — it simply waits for the final rendered state.
        // We intentionally do NOT wait for the spinner first because in some flows
        // (e.g. after OAuth redirect) the spinner may never appear in the DOM.
        await Page.Locator(".month-table, .day-view-container, .agenda-view-container").First.WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task SwitchToDayViewAsync()
    {
        await DayTab.ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SwitchToMonthViewAsync()
    {
        await MonthTab.ClickAsync();
        await MonthTable.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task SwitchToAgendaViewAsync()
    {
        await AgendaTab.ClickAsync();
        await AgendaViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task NavigateAgendaPrevMonthAsync()
    {
        var current = await GetAgendaCurrentMonthAsync();
        var expectedText = current.AddMonths(-1).ToString("MMMM yyyy");
        await Page.GetByTestId("agenda-prev-month").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
            .ToHaveTextAsync(expectedText, new() { Timeout = 30000 });
        await Page.WaitForTimeoutAsync(1000);
    }

    public async Task NavigateAgendaNextMonthAsync()
    {
        var current = await GetAgendaCurrentMonthAsync();
        var expectedText = current.AddMonths(1).ToString("MMMM yyyy");
        await Page.GetByTestId("agenda-next-month").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("agenda-month-year-label"))
            .ToHaveTextAsync(expectedText, new() { Timeout = 30000 });
        await Page.WaitForTimeoutAsync(1000);
    }

    public async Task<string> GetAgendaMonthYearTextAsync()
    {
        return (await Page.GetByTestId("agenda-month-year-label").InnerTextAsync()).Trim();
    }

    private async Task<DateTime> GetAgendaCurrentMonthAsync()
    {
        var text = await GetAgendaMonthYearTextAsync();
        return DateTime.ParseExact(text, "MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<int> GetAgendaDayRowCountAsync()
    {
        return await Page.Locator(".agenda-day-row").CountAsync();
    }

    public async Task<bool> HasTodayRowHighlightAsync()
    {
        return await Page.Locator(".agenda-today-row").CountAsync() == 1;
    }

    public async Task<bool> WeekendRowsHaveClassAsync()
    {
        // Wait for at least one row to be rendered before counting
        await Page.Locator(".agenda-weekend-row").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        // A month always has at least 8 weekend days
        return await Page.Locator(".agenda-weekend-row").CountAsync() >= 8;
    }

    public async Task<int> GetWeekdayRowsWithoutWeekendClassAsync()
    {
        // All .agenda-day-row rows that do NOT have .agenda-weekend-row
        var all = await Page.Locator(".agenda-day-row").CountAsync();
        var weekends = await Page.Locator(".agenda-weekend-row").CountAsync();
        return all - weekends;
    }

    public async Task<bool> IsAgendaCalendarHeaderVisibleAsync(string calendarName)
    {
        var header = Page.Locator("[data-testid^='agenda-calendar-header-']")
                         .Filter(new() { HasText = calendarName })
                         .First;
        try
        {
            await header.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsAgendaEventVisibleAsync(string expectedText, string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        return await cell.GetByText(expectedText, new() { Exact = false }).CountAsync() > 0;
    }

    public async Task<int> GetAgendaEventLineCountAsync(string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        return await cell.Locator(".agenda-event-line").CountAsync();
    }

    public async Task<string> GetAgendaOverflowTextAsync(string dateKey, Guid calendarId)
    {
        return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").InnerTextAsync();
    }

    public async Task<bool> IsAgendaOverflowVisibleAsync(string dateKey, Guid calendarId)
    {
        return await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").CountAsync() > 0;
    }

    public async Task TapAgendaEventAsync(string eventText, string dateKey, Guid calendarId)
    {
        var cell = Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}");
        await cell.GetByText(eventText, new() { Exact = false }).First.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaCellAsync(string dateKey, Guid calendarId)
    {
        // Click the cell itself (not an event line) to trigger the create modal
        await Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}").ClickAsync(
            new() { Position = new Position { X = 5, Y = 5 } });
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaFilledCellAsync(string dateKey, Guid calendarId)
    {
        // Click a cell that contains events — navigates to Day view
        await Page.GetByTestId($"agenda-cell-{dateKey}-{calendarId}").ClickAsync(
            new() { Position = new Position { X = 5, Y = 5 } });
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaOverflowAsync(string dateKey, Guid calendarId)
    {
        await Page.GetByTestId($"agenda-overflow-{dateKey}-{calendarId}").ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task TapAgendaCreateButtonAsync()
    {
        await Page.GetByTestId("agenda-create-button").ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task<string> GetModalStartDateValueAsync()
    {
        // Start date is the first date input in the modal, value format: "yyyy-MM-dd"
        var input = EventModal.Locator("input[type='date']").First;
        return await input.InputValueAsync();
    }

    public async Task<bool> IsCalendarChipActiveAsync(string calendarName)
    {
        var chip = EventModal.Locator(".chip").Filter(new() { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? "";
        return classes.Contains("chip-active");
    }

    public async Task OpenDayPickerAndGoAsync(string dateYyyyMmDd)
    {
        // Click the center date header button on Day view
        await DayPickerBtn.ClickAsync();
        await DayPickerModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        
        await DayPickerInput.FillAsync(dateYyyyMmDd);
        await DayPickerGoBtn.ClickAsync();
        
        // Ensure modal is gone before proceeding
        await DayPickerModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    public async Task ClickMoreEventsLinkAsync(string dayDateString)
    {
        // Click the +n more text on the month view
        var link = Page.GetByText(new Regex(@"^\+\d+ more$"));
        // Need to narrow down to the specific day's more link if provided, but since most tests only run on specific days we can just pick the first visible or use nth(0) assuming our tests are isolated.
        // Actually best is to find the cell by date. The cell has an id like `day-cell-2026-03-24`
        var cell = Page.Locator($"#day-cell-{dayDateString}");
        await cell.GetByTestId("overflow-indicator").ClickAsync();
        await DayViewContainer.WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }

    public async Task ClickDayGridSlotAsync(string calendarName, string timeString)
    {
        // Finds the specific calendar column and clicks roughly around the time slot
        var calHeader = Page.Locator(".calendar-header-col").Filter(new() { HasText = calendarName });
        // Since we know the index of the header, we can find the equivalent col in day-body-flex
        var idxTask = calHeader.EvaluateAsync<int>("el => Array.from(el.parentNode.children).indexOf(el) - 1"); // -1 for time-axis
        int colIndex = await idxTask;
        
        var col = Page.Locator(".calendar-col").Nth(colIndex);
        
        // Time string e.g. "10:00". Calculate Y offset... Actually just clicking the column is enough to open the modal!
        // The modal opens with start time based on the height clicked.
        // For E2E we might just want to trigger the event. Playwright allows clicking at X/Y offsets!
        var hParts = timeString.Split(':');
        int hours = int.Parse(hParts[0]);
        int minutes = int.Parse(hParts[1]);
        int totalMinutes = (hours * 60) + minutes;
        
        // The day body is 1440px tall (1px/minute).
        // Let's scroll into view first.
        var colBox = await col.BoundingBoxAsync();
        if (colBox != null)
        {
            await col.ClickAsync(new() { Position = new Position { X = 10, Y = totalMinutes } });
        }
        else 
        {
            await col.ClickAsync(); 
        }

        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
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
        await WaitForCalendarVisibleAsync();
    }

    public async Task WaitForWeatherStripAsync(int timeoutMs = 60000)
    {
        await Assertions.Expect(WeatherStrip).ToBeVisibleAsync(
            new() { Timeout = timeoutMs });
    }

    public async Task LoginAsync(string userName)
    {
        // Follow the full OAuth redirect chain:
        // /api/auth/login → simulator consent page → /api/auth/callback → /login-success → /
        await LoginBtn.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/oauth2/auth"), new() { Timeout = 30000 });
        await Page.Locator("select#selectedUserId").SelectOptionAsync(new SelectOptionValue { Label = userName });
        await Page.Locator("button[type='submit']").ClickAsync();
        await WaitForCalendarToLoadAsync();
    }

    public async Task SignOutAsync()
    {
        // The sign-out button moved to the Settings page (Task 11).
        // For test isolation, clear the auth token from localStorage directly
        // and reload to force the unauthenticated state.
        if (await IsSignedInAsync())
        {
            await Page.EvaluateAsync("() => { localStorage.clear(); sessionStorage.clear(); }");
            await Page.GotoAsync(_config.BaseUrl + "/");
            await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
            await LoginBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
        }
    }

    public async Task<bool> IsSignedInAsync()
    {
        // The dashboard header (brand + settings gear) is only rendered when authenticated.
        // When not authenticated, only the Login to Google button is shown.
        return await Page.Locator(".dashboard-header").CountAsync() > 0;
    }

    public async Task CreateEventAsync(string title)
    {
        await AddEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await FillAndSaveEventAsync(title);
    }

    /// <summary>
    /// Fills in the event title and saves when the modal is already open
    /// (e.g. after clicking a Day View grid slot).
    /// </summary>
    public async Task FillAndSaveEventAsync(string title)
    {
        await EventTitleInput.FillAsync(title);

        var eventsResponseTask = Page.WaitForResponseAsync(
            r => r.Url.Contains("api/calendars/events"),
            new() { Timeout = 30000 });

        await SaveEventBtn.ClickAsync();
        await EventModal.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
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
        await WaitForCalendarVisibleAsync();
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
        await WaitForCalendarVisibleAsync();
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
        await WaitForCalendarVisibleAsync();
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
    }

    /// <summary>
    /// Navigates to the next month only if <paramref name="date"/> falls outside the
    /// currently-visible month grid. The grid spans from the Sunday on or before the
    /// first of the current month to the Saturday on or after the last day of the month.
    /// </summary>
    public async Task NavigateToShowDateIfNeededAsync(DateTime date)
    {
        var today = DateTime.Today;
        var lastDayOfMonth = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)lastDayOfMonth.DayOfWeek + 7) % 7;
        var gridEnd = lastDayOfMonth.AddDays(daysUntilSaturday);

        if (date > gridEnd)
        {
            await NavigateToNextMonthAsync();
        }
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
        await WaitForCalendarVisibleAsync();
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
        await WaitForCalendarVisibleAsync();
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
        await Assertions.Expect(EventModal).ToBeHiddenAsync(new() { Timeout = 30000 });
        await eventsResponseTask;
        await WaitForCalendarVisibleAsync();
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
    /// <summary>
    /// Returns titles of all visible event capsules. Does NOT wait for the calendar
    /// to be fully rendered — this makes it safe for use inside polling loops
    /// (e.g. WaitForConditionAsync) where the page may be mid-re-render.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetVisibleEventsAsync()
    {
        // AllInnerTextsAsync captures all texts atomically in one call, avoiding the
        // TOCTOU race where CountAsync returns 1 but the element disappears before
        // InnerTextAsync can read it (causing a 30s auto-wait timeout).
        var texts = await EventCapsules.AllInnerTextsAsync();
        return texts;
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

    public async Task<int> GetCalendarHeaderCountAsync()
    {
        return await Page.Locator(".calendar-header-col").CountAsync();
    }

    public async Task WaitForAllDayEventVisibleAsync(string eventName)
    {
        await WaitForCalendarVisibleAsync();
        var capsule = Page.Locator($".all-day-col .event-capsule:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task WaitForTimedEventVisibleAsync(string eventName)
    {
        await WaitForCalendarVisibleAsync();
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
    }

    public async Task<double> GetTimedEventHeightAsync(string eventName)
    {
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        await capsule.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var style = await capsule.GetAttributeAsync("style") ?? "";
        
        // Extract height e.g., "height: 4.166666666666667%;"
        var match = Regex.Match(style, @"height:\s*([\d.]+)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double heightPercentage))
        {
            // The container is 1440px tall, so 1% = 14.4px
            return heightPercentage * 14.4;
        }
        return 0;
    }

    public async Task<double> GetTimedEventWidthPercentageAsync(string eventName)
    {
        var capsule = Page.Locator($".calendar-col .day-event-block:has-text('{eventName}')").First;
        var style = await capsule.GetAttributeAsync("style") ?? "";
        
        var match = Regex.Match(style, @"width:\s*calc\(([\d.]+)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out double widthPercentage))
        {
            return widthPercentage;
        }
        return 0;
    }

    public async Task<bool> IsCurrentTimeLineVisibleAsync()
    {
        return await CurrentTimeLine.IsVisibleAsync();
    }
}
