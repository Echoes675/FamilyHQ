using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CalendarsController : ControllerBase
{
    private readonly ICalendarRepository _calendarRepository;
    private readonly IGoogleCalendarClient _googleCalendarClient;
    private readonly ICalendarEventService _calendarEventService;
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(
        ICalendarRepository calendarRepository,
        IGoogleCalendarClient googleCalendarClient,
        ICalendarEventService calendarEventService,
        ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _googleCalendarClient = googleCalendarClient;
        _calendarEventService = calendarEventService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all synced calendars associated with the authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        return Ok(calendars);
    }

    /// <summary>
    /// Retrieves mapped calendar events for a specific month and year grid view.
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> GetEventsForMonth([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return BadRequest("Invalid year or month.");

        var firstDayOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var startDay = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek);

        var nextMonth = firstDayOfMonth.AddMonths(1);
        var endDay = nextMonth.AddDays(7 - (int)Math.Max(1, (int)nextMonth.DayOfWeek));

        var events = await _calendarRepository.GetEventsAsync(
            start: startDay,
            end: endDay.AddDays(14),
            ct: ct);

        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendarDict = calendars.ToDictionary(c => c.Id, c => c);

        var monthView = new MonthViewDto { Year = year, Month = month };

        foreach (var evt in events)
        {
            var primaryCal = evt.Calendars.FirstOrDefault(c => calendarDict.ContainsKey(c.Id));

            var vm = new CalendarEventViewModel
            {
                Id = evt.Id.ToString(),
                Title = evt.Title,
                StartTime = evt.Start.LocalDateTime,
                EndTime = evt.End.LocalDateTime,
                IsAllDay = evt.IsAllDay,
                Location = evt.Location,
                CalendarId = primaryCal?.Id ?? Guid.Empty,
                CalendarColor = primaryCal?.Color,
                CalendarName = primaryCal?.DisplayName,
                LinkedCalendars = evt.Calendars
                    .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
                    .ToList()
            };

            var dateKey = evt.Start.ToString("yyyy-MM-dd");
            if (!monthView.Days.ContainsKey(dateKey))
                monthView.Days[dateKey] = [];

            monthView.Days[dateKey].Add(vm);
        }

        return Ok(monthView);
    }

    /// <summary>
    /// Creates a new calendar event.
    /// </summary>
    [HttpPost("{calendarId:guid}/events")]
    public async Task<IActionResult> CreateEvent(Guid calendarId, [FromBody] Core.DTOs.CreateEventRequest request, CancellationToken ct)
    {
        // Validate the request
        var validator = new Core.Validators.CreateEventRequestValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        // Verify ownership via user-scoped query
        var userCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        if (!userCalendars.Any(c => c.Id == calendarId)) return NotFound("Calendar not found.");

        // Get a tracked instance so EF Core creates the join-table entry without re-inserting
        var calendar = await _calendarRepository.GetCalendarByIdAsync(calendarId, ct);
        if (calendar == null) return NotFound("Calendar not found.");

        var newEvent = new Core.Models.CalendarEvent
        {
            Title = request.Title,
            Start = request.Start,
            End = request.End,
            IsAllDay = request.IsAllDay,
            Location = request.Location,
            Description = request.Description
        };

        try
        {
            var createdEvent = await _googleCalendarClient.CreateEventAsync(calendar.GoogleCalendarId, newEvent, ct);
            createdEvent.Calendars.Add(calendar);
            await _calendarRepository.AddEventAsync(createdEvent, ct);
            await _calendarRepository.SaveChangesAsync(ct);

            return Ok(new CalendarEventViewModel
            {
                Id = createdEvent.Id.ToString(),
                Title = createdEvent.Title,
                StartTime = createdEvent.Start.LocalDateTime,
                EndTime = createdEvent.End.LocalDateTime,
                IsAllDay = createdEvent.IsAllDay,
                Location = createdEvent.Location,
                CalendarName = calendar.DisplayName,
                CalendarColor = calendar.Color,
                CalendarId = calendarId,
                LinkedCalendars = [new EventCalendarDto(calendar.Id, calendar.DisplayName, calendar.Color)]
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating event");
            return StatusCode(500, "Error communicating with Google Calendar");
        }
    }

    /// <summary>
    /// Updates an existing calendar event.
    /// </summary>
    [HttpPut("{calendarId:guid}/events/{eventId:guid}")]
    public async Task<IActionResult> UpdateEvent(Guid calendarId, Guid eventId, [FromBody] Core.DTOs.UpdateEventRequest request, CancellationToken ct)
    {
        // Validate the request
        var validator = new Core.Validators.CreateEventRequestValidator(); // UpdateEventRequest has the same validation rules
        var validationResult = await validator.ValidateAsync(new Core.DTOs.CreateEventRequest(
            request.CalendarInfoId,
            request.Title,
            request.Start,
            request.End,
            request.IsAllDay,
            request.Location,
            request.Description), ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.Errors);
        }

        var existing = await _calendarRepository.GetEventAsync(eventId, ct);
        if (existing == null || !existing.Calendars.Any(c => c.Id == calendarId)) return NotFound("Event not found.");

        var targetCalendar = existing.Calendars.First(c => c.Id == calendarId);

        existing.Title = request.Title;
        existing.Start = request.Start;
        existing.End = request.End;
        existing.IsAllDay = request.IsAllDay;
        existing.Location = request.Location;
        existing.Description = request.Description;

        try
        {
            var updatedGoogleEvent = await _googleCalendarClient.UpdateEventAsync(targetCalendar.GoogleCalendarId, existing, ct);
            existing.GoogleEventId = updatedGoogleEvent.GoogleEventId;
            await _calendarRepository.UpdateEventAsync(existing, ct);
            await _calendarRepository.SaveChangesAsync(ct);

            return Ok(new CalendarEventViewModel
            {
                Id = existing.Id.ToString(),
                Title = existing.Title,
                StartTime = existing.Start.LocalDateTime,
                EndTime = existing.End.LocalDateTime,
                IsAllDay = existing.IsAllDay,
                Location = existing.Location,
                CalendarName = targetCalendar.DisplayName,
                CalendarColor = targetCalendar.Color,
                CalendarId = calendarId,
                LinkedCalendars = existing.Calendars
                    .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating event");
            return StatusCode(500, "Error communicating with Google Calendar");
        }
    }

    /// <summary>
    /// Deletes an existing calendar event.
    /// </summary>
    [HttpDelete("{calendarId:guid}/events/{eventId:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid calendarId, Guid eventId, CancellationToken ct)
    {
        var existing = await _calendarRepository.GetEventAsync(eventId, ct);
        if (existing == null || !existing.Calendars.Any(c => c.Id == calendarId)) return NotFound("Event not found.");

        var targetCalendar = existing.Calendars.First(c => c.Id == calendarId);

        try
        {
            await _googleCalendarClient.DeleteEventAsync(targetCalendar.GoogleCalendarId, existing.GoogleEventId, ct);

            // Unlink from this calendar only
            existing.Calendars.Remove(targetCalendar);

            // Only delete the event entirely if it has no remaining calendar links
            if (!existing.Calendars.Any())
            {
                await _calendarRepository.DeleteEventAsync(eventId, ct);
            }
            else
            {
                await _calendarRepository.UpdateEventAsync(existing, ct);
            }

            await _calendarRepository.SaveChangesAsync(ct);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event");
            return StatusCode(500, "Error communicating with Google Calendar");
        }
    }

    /// <summary>
    /// Moves a calendar event from one calendar to another, applying any updated fields.
    /// </summary>
    [HttpPost("{calendarId:guid}/events/{eventId:guid}/reassign")]
    public async Task<IActionResult> ReassignEvent(
        Guid calendarId, Guid eventId,
        [FromBody] ReassignEventRequest request,
        CancellationToken ct)
    {
        var updated = await _calendarEventService.ReassignAsync(calendarId, eventId, request, ct);
        if (updated is null)
            return NotFound("Event not found or calendar mismatch.");

        var toCalendar = updated.Calendars.First();
        return Ok(new CalendarEventViewModel
        {
            Id = updated.Id.ToString(),
            Title = updated.Title,
            StartTime = updated.Start.LocalDateTime,
            EndTime = updated.End.LocalDateTime,
            IsAllDay = updated.IsAllDay,
            Location = updated.Location,
            CalendarId = toCalendar.Id,
            CalendarName = toCalendar.DisplayName,
            CalendarColor = toCalendar.Color,
            LinkedCalendars = updated.Calendars
                .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color))
                .ToList()
        });
    }
}
