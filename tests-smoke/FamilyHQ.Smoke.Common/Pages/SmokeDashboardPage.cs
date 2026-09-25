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
        await CommitFieldAsync(TitleInput, draft.Title);
        await CommitFieldAsync(DescriptionInput, draft.Description);

        if (draft.Location is not null)
        {
            await CommitFieldAsync(LocationInput, draft.Location);
        }

        await SetDateRangeAsync(draft.Date);

        // Start before end, and never the other way round. The modal's start-time setter preserves the
        // event's duration by shifting the end by the same delta (moving 09:00→10:00 drags a 10:00 end to
        // 11:00), so a start set afterwards would silently move an end that was already correct. Setting
        // the end second is safe because its setter is absolute.
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
        await CommitFieldAsync(TitleInput, newTitle);
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

    /// <summary>
    /// Types a value into a Blazor-bound field <b>and makes it stick</b>.
    /// <para>
    /// Playwright's <c>FillAsync</c> sets the value and raises <c>input</c>, but the <c>change</c> event that
    /// Blazor's <c>@bind</c> and <c>@onchange</c> actually listen for does not follow until the field is
    /// blurred. Elsewhere in a test suite that is invisible: the next thing a flow does is usually click
    /// another control, the click blurs the field, <c>change</c> fires, and the value commits just in time.
    /// It is sequencing luck, and FHQ-141's first preprod run is what it looks like when the luck runs out —
    /// the value sat in the DOM while the component's model kept its old one.
    /// </para>
    /// <para>
    /// Pressing Tab makes the commit explicit and ordered, so no field depends on what happens to it next.
    /// </para>
    /// </summary>
    private static async Task CommitFieldAsync(ILocator field, string value)
    {
        await field.FillAsync(value);
        await field.PressAsync("Tab");
    }

    private async Task SetDateRangeAsync(DateOnly date)
    {
        var value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateInputs = EventModal.Locator("input[type='date']");

        await CommitFieldAsync(dateInputs.Nth(0), value);
        await CommitFieldAsync(dateInputs.Nth(1), value);

        // Read back from the DOM, which after a commit is re-rendered from the component's model — so this
        // is a check that the model took the date, not merely that the keystrokes landed.
        foreach (var index in new[] { 0, 1 })
        {
            var actual = await dateInputs.Nth(index).InputValueAsync();
            if (!string.Equals(actual, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The event modal did not accept {value} as its "
                    + $"{(index == 0 ? "start" : "end")} date; it reads '{actual}'.");
            }
        }
    }

    /// <summary>
    /// Sets one of the two time pickers and confirms the component's <b>model</b> took the value.
    /// <para>
    /// The stepper readouts are rendered from the model, and the text box is not — so asserting the
    /// readouts is the only way to tell "the value committed" from "the value is merely on screen". That
    /// distinction is the whole bug: a typed value that never committed left the picker showing 10:00 over
    /// a model still holding 09:00, and the scenario went on to fail somewhere else entirely.
    /// </para>
    /// <para>
    /// On failure it says what it wanted, what the model holds and what the box shows, rather than leaving
    /// a bare locator timeout for someone to reverse-engineer from a screenshot.
    /// </para>
    /// </summary>
    private async Task SetTimeAsync(string which, TimeOnly time)
    {
        var picker = EventModal.GetByTestId($"{which}-time-picker");
        var textField = picker.Locator("input[type='text']");
        var displays = picker.Locator(".time-picker-display");

        var wanted = time.ToString("HH\\:mm", CultureInfo.InvariantCulture);
        await CommitFieldAsync(textField, wanted);

        try
        {
            await Assertions.Expect(displays.Nth(0))
                .ToHaveTextAsync(time.Hour.ToString("00", CultureInfo.InvariantCulture));
            await Assertions.Expect(displays.Nth(1))
                .ToHaveTextAsync(time.Minute.ToString("00", CultureInfo.InvariantCulture));
        }
        catch (PlaywrightException ex)
        {
            var model = $"{(await displays.Nth(0).InnerTextAsync()).Trim()}"
                        + $":{(await displays.Nth(1).InnerTextAsync()).Trim()}";
            var shown = await textField.InputValueAsync();
            var classes = await textField.GetAttributeAsync("class") ?? string.Empty;

            throw new InvalidOperationException(
                $"The {which} time picker did not accept {wanted}. Its steppers — which render the "
                + $"component's model — read {model}, and its text box reads '{shown}'"
                + (classes.Contains("is-invalid", StringComparison.Ordinal)
                    ? " and is flagged invalid, so the value was rejected as unparseable."
                    : ", so the typed value never committed to the model.")
                + " Nothing downstream of this can be trusted, so the scenario stops here.",
                ex);
        }
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
        await AssertSaveIsNotBlockedAsync(what);

        var writeResponse = Page.WaitForResponseAsync(
            response => response.Url.Contains("/api/events", StringComparison.Ordinal)
                        && string.Equals(response.Request.Method, method, StringComparison.Ordinal),
            new PageWaitForResponseOptions { Timeout = configuration.DefaultTimeoutMs });

        await SaveButton.ClickAsync();
        await ConfirmScopePromptIfShownAsync();

        IResponse response;
        try
        {
            response = await writeResponse;
        }
        catch (TimeoutException ex)
        {
            // The modal swallowed the click. Say what it is showing now, rather than reporting only that no
            // request arrived within thirty seconds.
            throw new InvalidOperationException(
                $"The kiosk never issued the {method} for '{what}'. The modal is still showing: "
                + await DescribeModalStateAsync(),
                ex);
        }

        await AssertWriteSucceededAsync(response, what);
        await EventModal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await WaitForCalendarVisibleAsync();
    }

    /// <summary>
    /// Refuses to click a Save the modal has disabled.
    /// <para>
    /// The modal gates Save on its own validation — an unselected calendar, a repeat that has been switched
    /// on without a frequency — and a disabled button swallows the click silently. Without this the symptom
    /// arrives much later and somewhere else: a thirty-second wait for a request that was never going to be
    /// made, or an assertion about an event that was never created. Checking first turns that into one
    /// sentence naming the control that is incomplete.
    /// </para>
    /// </summary>
    private async Task AssertSaveIsNotBlockedAsync(string what)
    {
        if (await SaveButton.IsEnabledAsync())
        {
            return;
        }

        throw new InvalidOperationException(
            $"The kiosk will not let the suite {what}: Save is disabled. {await DescribeModalStateAsync()}");
    }

    /// <summary>
    /// What the modal is currently complaining about, in one line. Best-effort — a diagnostic that threw
    /// would replace the real failure with a worse one.
    /// </summary>
    private async Task<string> DescribeModalStateAsync()
    {
        var parts = new List<string>();

        try
        {
            foreach (var tab in new[] { "details", "repeat" })
            {
                if (await EventModal.GetByTestId($"event-modal-tab-{tab}-incomplete").CountAsync() > 0)
                {
                    parts.Add($"the {tab} tab is marked incomplete");
                }
            }

            var hint = EventModal.GetByTestId("event-save-hint");
            if (await hint.CountAsync() > 0)
            {
                parts.Add($"save hint: '{(await hint.InnerTextAsync()).Trim()}'");
            }

            var error = EventModal.GetByTestId("event-modal-error");
            if (await error.CountAsync() > 0)
            {
                parts.Add($"error banner: '{(await error.InnerTextAsync()).Trim()}'");
            }

            var activeChips = await EventModal.Locator(".chip-active").CountAsync();
            parts.Add($"{activeChips} calendar chip(s) selected");
        }
        catch (PlaywrightException ex)
        {
            parts.Add($"(modal state could not be read: {ex.Message})");
        }

        return parts.Count == 0 ? "no validation markers are showing." : string.Join("; ", parts) + ".";
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
