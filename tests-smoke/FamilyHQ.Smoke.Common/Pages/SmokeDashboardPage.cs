using System.Globalization;
using System.Text.RegularExpressions;
using FamilyHQ.Smoke.Common.Configuration;
using Microsoft.Playwright;

namespace FamilyHQ.Smoke.Common.Pages;

/// <summary>
/// The preprod kiosk, as the smoke suite drives it.
/// <para>
/// A deliberately small page object: the smoke suite is not a second E2E suite, so it only knows how
/// to do the handful of things the third-party scenarios need — open the dashboard, read the weather
/// strip, create / rename / delete an event, create a bounded weekly series, and look at the Day view.
/// Everything else about the UI stays E2E's business, and this class is a standalone duplicate of the
/// parts it needs rather than a reference into that suite.
/// </para>
/// <para>
/// Assertions live in the step definitions. What lives here is the knowledge of which control is which,
/// and the waits that make an interaction deterministic — never a wait that papers over a failure.
/// </para>
/// </summary>
public sealed class SmokeDashboardPage(IPage page, SmokeConfiguration configuration) : SmokeBasePage(page)
{
    private const int TileRenderTimeoutMs = 30000;
    private const int MaxStepperClicks = 60;

    private static readonly Regex ActiveChipClass = new("chip-active", RegexOptions.Compiled);

    public override string PageUrl => configuration.BaseUrl.TrimEnd('/') + "/";

    // ── Dashboard chrome ────────────────────────────────────────────────────────

    public ILocator WeatherStrip => Page.GetByTestId("weather-strip");

    public ILocator WeatherCurrentConditions => Page.GetByTestId("weather-strip-current");

    public ILocator WeatherTemperature => Page.GetByTestId("weather-strip-temp");

    public ILocator WeatherCondition => Page.GetByTestId("weather-strip-condition");

    public ILocator DayViewTiles => Page.GetByTestId("day-event-block");

    public ILocator AllDayTiles => Page.GetByTestId("event-capsule");

    public ILocator RecurrenceIndicators => Page.GetByTestId("recurrence-indicator");

    // ── Modal ───────────────────────────────────────────────────────────────────

    private ILocator EventModal => Page.Locator(".modal-content");

    private ILocator TitleInput => EventModal.GetByTestId("event-title-input");

    private ILocator DescriptionInput => EventModal.GetByTestId("event-description-input");

    private ILocator LocationInput => EventModal.GetByTestId("event-location-input");

    private ILocator SaveButton => EventModal.GetByTestId("event-save-btn");

    private ILocator DeleteButton => EventModal.GetByTestId("event-delete-btn");

    private ILocator RecurrenceSection => EventModal.GetByTestId("recurrence-section");

    private ILocator RepeatToggle => RecurrenceSection.GetByTestId("recurrence-repeat-toggle");

    private ILocator CountInput => RecurrenceSection.GetByTestId("recurrence-count");

    private ILocator ScopePrompt => Page.GetByTestId("recurrence-scope-prompt");

    private ILocator ScopePromptConfirm => Page.GetByTestId("recurrence-scope-ok");

    /// <summary>
    /// Opens the dashboard and waits for the calendar's first events response, so a scenario never
    /// asserts against an empty grid that is merely still loading.
    /// </summary>
    public async Task OpenAsync()
    {
        var eventsResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("api/calendars/events", StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await NavigateAsync();
        await eventsResponse;
        await WaitForCalendarVisibleAsync();
    }

    public Task WaitForWeatherStripAsync(int timeoutMs) =>
        Assertions.Expect(WeatherStrip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = timeoutMs });

    /// <summary>Switches to the Day view and moves it to <paramref name="date"/>.</summary>
    public async Task GoToDayAsync(DateOnly date)
    {
        await Page.GetByTestId("day-tab").ClickAsync();
        await Page.Locator(".day-view-container")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await Page.GetByTestId("day-picker-btn").ClickAsync();
        var picker = Page.Locator(".modal-backdrop").Filter(new LocatorFilterOptions { HasText = "Select Date" });
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await Page.GetByTestId("day-picker-input")
            .FillAsync(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await Page.GetByTestId("day-picker-go-btn").ClickAsync();

        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>Every event title currently rendered on the Day view, timed tiles and all-day tiles alike.</summary>
    public async Task<IReadOnlyList<string>> GetDayViewTitlesAsync()
    {
        var timed = await DayViewTiles.AllInnerTextsAsync();
        var allDay = await AllDayTiles.AllInnerTextsAsync();
        return [.. timed, .. allDay];
    }

    /// <summary>
    /// Waits until a tile whose text contains <paramref name="titleFragment"/> is on the Day view for
    /// <paramref name="date"/>. Day view rather than Month view on purpose: the month grid renders at
    /// most three tiles per day, and preprod keeps every event every smoke run has ever created.
    /// </summary>
    public async Task WaitForEventOnDayAsync(string titleFragment, DateOnly date)
    {
        await GoToDayAsync(date);
        await TileWithText(titleFragment).First
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TileRenderTimeoutMs
            });
    }

    /// <summary>True when the tile carrying <paramref name="titleFragment"/> also carries the recurring glyph.</summary>
    public async Task<bool> TileIsMarkedRecurringAsync(string titleFragment)
    {
        var tile = TileWithText(titleFragment).First;
        await tile.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = TileRenderTimeoutMs
        });
        return await tile.GetByTestId("recurrence-indicator").CountAsync() > 0;
    }

    /// <summary>
    /// Creates an event through the kiosk exactly as a family member would, then waits for the create
    /// to be accepted. A non-success status is raised immediately rather than surfacing later as a
    /// "modal never closed" timeout.
    /// </summary>
    public async Task CreateEventAsync(SmokeEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await Page.GetByTestId("add-event-btn").ClickAsync();
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ShowTabAsync("details");
        await TitleInput.FillAsync(draft.Title);
        await DescriptionInput.FillAsync(draft.Description);

        if (draft.Location is not null)
        {
            await LocationInput.FillAsync(draft.Location);
        }

        await SetDateRangeAsync(draft.Date);
        await SetTimeAsync("start", draft.StartTime);
        await SetTimeAsync("end", draft.EndTime);

        foreach (var calendarName in draft.CalendarNames)
        {
            await ActivateCalendarChipAsync(calendarName);
        }

        if (draft.Recurrence is not null)
        {
            await SetBoundedWeeklyRepeatAsync(draft.Recurrence);
        }

        await SaveAndAwaitWriteAsync("POST", $"create '{draft.Title}'");
    }

    /// <summary>
    /// Changes an event's title on the kiosk and <b>nothing else</b> — the golden-rule scenario. No
    /// other control in the modal is touched, so whatever Google holds for the other fields is whatever
    /// FamilyHQ chooses to send back for them.
    /// </summary>
    public async Task RenameEventAsync(string currentTitleFragment, string newTitle, DateOnly date)
    {
        await OpenEventAsync(currentTitleFragment, date);
        await ShowTabAsync("details");
        await TitleInput.FillAsync(newTitle);
        await SaveAndAwaitWriteAsync("PUT", $"rename to '{newTitle}'");
    }

    /// <summary>Deletes an event from the kiosk.</summary>
    public async Task DeleteEventAsync(string titleFragment, DateOnly date)
    {
        await OpenEventAsync(titleFragment, date);

        var deleteResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("/api/events", StringComparison.Ordinal)
                        && string.Equals(response.Request.Method, "DELETE", StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await DeleteButton.ClickAsync();
        await ConfirmScopePromptIfShownAsync();
        await AssertWriteSucceededAsync(await deleteResponse, $"delete '{titleFragment}'");
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    // ── Internals ───────────────────────────────────────────────────────────────

    private ILocator TileWithText(string titleFragment) =>
        Page.Locator("[data-testid='day-event-block'], [data-testid='event-capsule']")
            .Filter(new LocatorFilterOptions { HasText = titleFragment });

    private Task WaitForCalendarVisibleAsync() =>
        Page.Locator(".month-table, .day-view-container, .agenda-view-container").First
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = TileRenderTimeoutMs
            });

    private async Task OpenEventAsync(string titleFragment, DateOnly date)
    {
        await GoToDayAsync(date);
        await TileWithText(titleFragment).First.ClickAsync(new LocatorClickOptions
        {
            Timeout = TileRenderTimeoutMs
        });
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    private async Task ShowTabAsync(string tab)
    {
        var tabButton = EventModal.GetByTestId($"event-modal-tab-{tab}");
        await tabButton.ClickAsync();
        await Assertions.Expect(EventModal.GetByTestId($"event-modal-panel-{tab}")).ToBeVisibleAsync();
    }

    private async Task SetDateRangeAsync(DateOnly date)
    {
        var value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateInputs = EventModal.Locator("input[type='date']");
        await dateInputs.Nth(0).FillAsync(value);
        await dateInputs.Nth(1).FillAsync(value);
    }

    private async Task SetTimeAsync(string which, TimeOnly time)
    {
        // The time picker's text field accepts HH:mm and commits on change, which is the one affordance
        // that does not depend on how many times a stepper has to be pressed.
        var textField = EventModal.GetByTestId($"{which}-time-picker").Locator("input[type='text']");
        await textField.FillAsync(time.ToString("HH\\:mm", CultureInfo.InvariantCulture));
        await Assertions.Expect(EventModal.GetByTestId($"{which}-time-picker").Locator(".time-picker-display").Nth(0))
            .ToHaveTextAsync(time.Hour.ToString("00", CultureInfo.InvariantCulture));
    }

    private async Task ActivateCalendarChipAsync(string calendarName)
    {
        await ShowTabAsync("details");
        var chip = EventModal.Locator(".chip").Filter(new LocatorFilterOptions { HasText = calendarName });
        var classes = await chip.GetAttributeAsync("class") ?? string.Empty;
        if (!classes.Contains("chip-active", StringComparison.Ordinal))
        {
            await chip.ClickAsync();
        }

        await Assertions.Expect(chip).ToHaveClassAsync(ActiveChipClass);
    }

    /// <summary>
    /// Drives the recurrence picker to a bounded weekly rule. The Custom drawer is the only place the
    /// picker offers an end condition, so a bounded series is created there by construction — the
    /// Weekly preset would produce an endless one.
    /// </summary>
    private async Task SetBoundedWeeklyRepeatAsync(SmokeWeeklyRecurrence recurrence)
    {
        await ShowTabAsync("repeat");

        if (await RepeatToggle.GetAttributeAsync("aria-pressed") != "true")
        {
            await RepeatToggle.ClickAsync();
            await Assertions.Expect(RepeatToggle).ToHaveAttributeAsync("aria-pressed", "true");
        }

        await PressPillAsync(RecurrenceSection.GetByTestId("recurrence-mode-custom"));
        await PressPillAsync(RecurrenceSection.GetByTestId("recurrence-frequency-weekly"));

        if (recurrence.Weekdays.Count > 0)
        {
            await SetWeekdaysAsync(recurrence.Weekdays);
        }

        await PressPillAsync(RecurrenceSection.GetByTestId("recurrence-end-count"));
        await SetOccurrenceCountAsync(recurrence.Occurrences);
    }

    private static async Task PressPillAsync(ILocator pill)
    {
        await pill.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await pill.ClickAsync();
        await Assertions.Expect(pill).ToHaveAttributeAsync("aria-pressed", "true");
    }

    /// <summary>
    /// Leaves exactly <paramref name="weekdays"/> pressed. Choosing Custom + Weekly pre-selects the
    /// start date's weekday, so the unwanted ones have to be switched off — asserting "exactly Google's
    /// instances" later means the rule sent must be exactly the rule asked for.
    /// </summary>
    private async Task SetWeekdaysAsync(IReadOnlyList<DayOfWeek> weekdays)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var toggle = RecurrenceSection.GetByTestId($"recurrence-weekday-{day}");
            var wanted = weekdays.Contains(day);
            var pressed = await toggle.GetAttributeAsync("aria-pressed") == "true";
            if (pressed != wanted)
            {
                await toggle.ClickAsync();
            }

            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", wanted ? "true" : "false");
        }
    }

    /// <summary>
    /// Steps the occurrence count to <paramref name="occurrences"/> using the picker's own +/- buttons
    /// rather than typing into the field, because the stepper is what a kiosk user has and it commits
    /// on every click.
    /// </summary>
    private async Task SetOccurrenceCountAsync(int occurrences)
    {
        var increment = RecurrenceSection.GetByTestId("recurrence-count-increment");
        var decrement = RecurrenceSection.GetByTestId("recurrence-count-decrement");

        for (var click = 0; click < MaxStepperClicks; click++)
        {
            var current = int.Parse(await CountInput.InputValueAsync(), CultureInfo.InvariantCulture);
            if (current == occurrences)
            {
                return;
            }

            await (current < occurrences ? increment : decrement).ClickAsync();
        }

        throw new InvalidOperationException(
            $"The recurrence count stepper did not reach {occurrences} within {MaxStepperClicks} clicks.");
    }

    private async Task SaveAndAwaitWriteAsync(string method, string what)
    {
        var writeResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("/api/events", StringComparison.Ordinal)
                        && string.Equals(response.Request.Method, method, StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await SaveButton.ClickAsync();
        await ConfirmScopePromptIfShownAsync();
        await AssertWriteSucceededAsync(await writeResponse, what);
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>
    /// Accepts the recurrence scope prompt at its default ("all events in the series") when the kiosk
    /// shows one. It only appears for a series, and the smoke scenarios that touch a series mean the
    /// whole series, so there is no scope to choose.
    /// </summary>
    private async Task ConfirmScopePromptIfShownAsync()
    {
        if (await ScopePrompt.IsVisibleAsync())
        {
            await ScopePromptConfirm.ClickAsync();
        }
    }

    private static async Task AssertWriteSucceededAsync(IResponse response, string what)
    {
        if (response.Status >= 400)
        {
            throw new InvalidOperationException(
                $"The kiosk refused to {what}: the API answered HTTP {response.Status} to "
                + $"{response.Request.Method} {response.Url}. The modal stays open on an error, so "
                + "without this the symptom would be an opaque timeout.");
        }
    }
}
