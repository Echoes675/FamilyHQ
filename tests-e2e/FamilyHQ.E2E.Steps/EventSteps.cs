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

        var attendeeCalendar = isolatedTemplate.Calendars.Find(c => c.Summary == calendarName);
        if (attendeeCalendar == null)
            throw new InvalidOperationException($"Calendar '{calendarName}' not found in template.");

        // Find the existing event by name and append the attendee calendar ID.
        var existingEvent = isolatedTemplate.Events.Find(e => e.Summary == eventName);
        if (existingEvent == null)
            throw new InvalidOperationException($"Event '{eventName}' not found in template — add it before calling this step.");

        existingEvent.AttendeeCalendarIds.Add(attendeeCalendar.Id);

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
