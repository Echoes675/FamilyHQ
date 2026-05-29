using System;
using System.Threading.Tasks;
using FamilyHQ.E2E.Data.Api;
using FamilyHQ.E2E.Data.Models;
using Reqnroll;

namespace FamilyHQ.E2E.Steps;

using FamilyHQ.E2E.Common.Helpers;

[Binding]
public class EventSteps
{
    private readonly ScenarioContext _scenarioContext;
    private readonly SimulatorApiClient _simulatorApi;

    public EventSteps(ScenarioContext scenarioContext, SimulatorApiClient simulatorApi)
    {
        _scenarioContext = scenarioContext;
        _simulatorApi = simulatorApi;
    }

    [Given(@"the user has an all-day event ""([^""]*)"" tomorrow")]
    public async Task GivenTheUserHasAnAll_DayEventTomorrow(string eventName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();
        
        var tomorrow = DateTime.Today.AddDays(1);
        
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = tomorrow,
            EndTime = tomorrow.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" on ""([^""]*)""")]
    public async Task GivenTheUserHasAnAllDayEventOn(string eventName, string dateExpr)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        var eventDate = DateTime.ParseExact(
            DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", null);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = eventDate,
            EndTime = eventDate.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" in (\d+) days")]
    public async Task GivenTheUserHasAnAllDayEventInDays(string eventName, int days)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();
        
        var eventDate = DateTime.Today.AddDays(days);
        
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = eventDate,
            EndTime = eventDate.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" tomorrow in ""([^""]*)""")]
    public async Task GivenTheUserHasAnAllDayEventTomorrowInCalendar(string eventName, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");

        // Find the calendar ID by name
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName);
        if (calendar == null)
        {
            throw new InvalidOperationException($"Calendar '{calendarName}' not found in template.");
        }

        var tomorrow = DateTime.Today.AddDays(1);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = tomorrow,
            EndTime = tomorrow.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" in (\d+) days in ""([^""]*)""")]
    public async Task GivenTheUserHasAnAllDayEventInDaysInCalendar(string eventName, int days, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var eventDate = DateTime.Today.AddDays(days);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = eventDate,
            EndTime = eventDate.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has a timed event ""([^""]*)"" at ""([^""]*)"" in (\d+) days in ""([^""]*)""")]
    public async Task GivenTheUserHasATimedEventAtInDaysInCalendar(
        string eventName, string timeStr, int days, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.Today.AddDays(days);
        var timeParts = timeStr.Split(':');
        var startTime = date.AddHours(int.Parse(timeParts[0])).AddMinutes(int.Parse(timeParts[1]));

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = startTime,
            EndTime = startTime.AddHours(1),
            IsAllDay = false
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has (\d+) events in (\d+) days in ""([^""]*)""")]
    public async Task GivenTheUserHasNEventsInDaysInCalendar(int count, int days, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.Today.AddDays(days);

        for (int i = 0; i < count; i++)
        {
            var startTime = date.AddHours(8 + i);
            isolatedTemplate.Events.Add(new SimulatorEventModel
            {
                Id = "evt_" + Guid.NewGuid().ToString("N"),
                CalendarId = calendar.Id,
                Summary = $"Event {i + 1}",
                StartTime = startTime,
                EndTime = startTime.AddHours(1),
                IsAllDay = false
            });
        }

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has the event ""([^""]*)"" also in ""([^""]*)""")]
    public async Task GivenTheUserHasTheEventAlsoInCalendar(string eventName, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");

        var additionalCalendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
            ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found in template.");

        var existingEvent = isolatedTemplate.Events.Find(e => e.Summary == eventName)
            ?? throw new InvalidOperationException(
                $"Event '{eventName}' not found in template — add it before calling this step.");

        // Locate the shared calendar — multi-member events live on the shared calendar
        var sharedCalendar = isolatedTemplate.Calendars.Find(c => c.IsShared)
            ?? throw new InvalidOperationException(
                "No shared calendar in the user template. Add a calendar with IsShared: true.");

        // Find the original member calendar by the event's current CalendarId
        var originalCalendar = isolatedTemplate.Calendars.Find(c => c.Id == existingEvent.CalendarId)
            ?? throw new InvalidOperationException(
                $"Original calendar for event '{eventName}' not found in template.");

        if (originalCalendar.IsShared)
            throw new InvalidOperationException(
                $"Event '{eventName}' is already on the shared calendar. " +
                "Ensure it is seeded on an individual member calendar before calling this step.");

        // Move the event to the shared calendar and encode both members in the description tag
        existingEvent.CalendarId = sharedCalendar.Id;
        existingEvent.Description = $"[members: {originalCalendar.Summary}, {additionalCalendar.Summary}]";

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has a timed event ""([^""]*)"" tomorrow at ""([^""]*)"" for (\d+) hour")]
    public Task GivenTheUserHasATimedEventTomorrowAtForHour(string eventName, string timeStr, int durationHours)
        => AddTimedEvent(eventName, timeStr, durationHours * 60);

    [Given(@"the user has a timed event ""([^""]*)"" tomorrow at ""([^""]*)"" for (\d+) minutes")]
    public Task GivenTheUserHasATimedEventTomorrowAtForMinutes(string eventName, string timeStr, int durationMinutes)
        => AddTimedEvent(eventName, timeStr, durationMinutes);

    private async Task AddTimedEvent(string eventName, string timeStr, int durationMinutes)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        var tomorrow = DateTime.Today.AddDays(1);
        var timeParts = timeStr.Split(':');
        var hour = int.Parse(timeParts[0]);
        var minute = int.Parse(timeParts[1]);

        var startTime = tomorrow.AddHours(hour).AddMinutes(minute);
        var endTime = startTime.AddMinutes(durationMinutes);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = startTime,
            EndTime = endTime,
            IsAllDay = false
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" spanning (\d+) days starting tomorrow")]
    public async Task GivenTheUserHasAnAllDayEventSpanningDaysStartingTomorrow(string eventName, int days)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();
        
        var tomorrow = DateTime.Today.AddDays(1);
        
        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = tomorrow,
            EndTime = tomorrow.AddDays(days), // E.g., spanning 2 days ends logic
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has a timed event ""([^""]*)"" starting tomorrow at ""([^""]*)"" spanning (\d+) days")]
    public async Task GivenTheUserHasATimedEventStartingTomorrowAtSpanningDays(string eventName, string timeStr, int days)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendarId = _scenarioContext.GetCurrentCalendarId();

        var tomorrow = DateTime.Today.AddDays(1);
        var timeParts = timeStr.Split(':');
        var hour = int.Parse(timeParts[0]);
        var minute = int.Parse(timeParts[1]);

        var startTime = tomorrow.AddHours(hour).AddMinutes(minute);
        var endTime = startTime.AddDays(days); // E.g. 10:00 AM tomorrow to 10:00 AM next day (spans 1 24h period)

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendarId,
            Summary = eventName,
            StartTime = startTime,
            EndTime = endTime,
            IsAllDay = false
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has a timed event ""([^""]*)"" at ""([^""]*)"" on ""([^""]*)"" in ""([^""]*)""")]
    public async Task GivenTheUserHasATimedEventAtOnInCalendar(
        string eventName, string timeStr, string dateExpr, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.ParseExact(DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var timeParts = timeStr.Split(':');
        var startTime = date.AddHours(int.Parse(timeParts[0])).AddMinutes(int.Parse(timeParts[1]));

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = startTime,
            EndTime = startTime.AddHours(1),
            IsAllDay = false
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    [Given(@"the user has an all-day event ""([^""]*)"" on ""([^""]*)"" in ""([^""]*)""")]
    public async Task GivenTheUserHasAnAllDayEventOnInCalendar(
        string eventName, string dateExpr, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.ParseExact(DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = date,
            EndTime = date.AddDays(1),
            IsAllDay = true
        });

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    // FHQ-18.11: seeds a Google recurring SERIES — a single master event carrying a weekly RRULE.
    // The series starts TOMORROW so its first occurrence is always inside the visible window
    // (Rule 9, relative dates), and repeats on tomorrow's weekday. The Simulator expands the
    // master into one INSTANCE per occurrence on events.list, so after a sync the dashboard shows
    // N occurrences. The weekday-derived subtitle text (e.g. "Repeats weekly on Tuesday") is
    // stashed for the details assertion so the scenario never hardcodes a weekday.
    [Given(@"the user has a weekly recurring event ""([^""]*)"" for (\d+) occurrences in ""([^""]*)""")]
    public async Task GivenTheUserHasAWeeklyRecurringEventForOccurrences(
        string eventName, int occurrences, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var firstOccurrence = DateTime.Today.AddDays(1).AddHours(18); // tomorrow, 18:00 timed event
        var weekday = firstOccurrence.DayOfWeek;
        var rrule = $"RRULE:FREQ=WEEKLY;BYDAY={WeekdayCode(weekday)};COUNT={occurrences}";

        isolatedTemplate.Events.Add(new SimulatorEventModel
        {
            Id = "evt_" + Guid.NewGuid().ToString("N"),
            CalendarId = calendar.Id,
            Summary = eventName,
            StartTime = firstOccurrence,
            EndTime = firstOccurrence.AddHours(1),
            IsAllDay = false,
            RecurrenceRule = rrule
        });

        // Surface derived expectations so view-specific Then steps stay weekday-agnostic.
        _scenarioContext["RecurringSeriesFirstOccurrenceDate"] = firstOccurrence.Date;
        _scenarioContext["RecurringSeriesExpectedSubtitle"] =
            $"Repeats weekly on {weekday}"; // DayOfWeek.ToString() is the English weekday name

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }

    // Navigates the Day view's day picker to the recurring series' first occurrence date (tomorrow),
    // the day guaranteed to carry the first instance.
    [When(@"I select the recurring event's first occurrence in the day picker")]
    public async Task WhenISelectTheRecurringEventsFirstOccurrenceInTheDayPicker()
    {
        var date = _scenarioContext.Get<DateTime>("RecurringSeriesFirstOccurrenceDate");
        var page = _scenarioContext.Get<Microsoft.Playwright.IPage>();
        var dashboardPage = new FamilyHQ.E2E.Common.Pages.DashboardPage(page);
        await dashboardPage.OpenDayPickerAndGoAsync(date.ToString("yyyy-MM-dd"));
    }

    private static string WeekdayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "SU",
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown weekday.")
    };

    [Given(@"the user has (\d+) events on ""([^""]*)"" in ""([^""]*)""")]
    public async Task GivenTheUserHasNEventsOnInCalendar(int count, string dateExpr, string calendarName)
    {
        var isolatedTemplate = _scenarioContext.Get<SimulatorConfigurationModel>("UserTemplate");
        var calendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName)
                       ?? throw new InvalidOperationException($"Calendar '{calendarName}' not found.");

        var date = DateTime.ParseExact(DateExpressionResolver.Resolve(dateExpr), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        for (int i = 0; i < count; i++)
        {
            var startTime = date.AddHours(8 + i);
            isolatedTemplate.Events.Add(new SimulatorEventModel
            {
                Id = "evt_" + Guid.NewGuid().ToString("N"),
                CalendarId = calendar.Id,
                Summary = $"Event {i + 1}",
                StartTime = startTime,
                EndTime = startTime.AddHours(1),
                IsAllDay = false
            });
        }

        await _simulatorApi.ConfigureUserTemplateAsync(isolatedTemplate);
    }
}
