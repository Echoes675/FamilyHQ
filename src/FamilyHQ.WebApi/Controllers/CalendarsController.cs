using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CalendarsController : ControllerBase
{
    private readonly ICalendarRepository _calendarRepository;
    private readonly IGoogleCalendarClient _googleCalendarClient;
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(
        ICalendarRepository calendarRepository, 
        IGoogleCalendarClient googleCalendarClient,
        ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _googleCalendarClient = googleCalendarClient;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all synced calendars associated with the authenticated user.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of CalendarInfo items.</returns>
    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        return Ok(calendars);
    }

    /// <summary>
    /// Retrieves mapped calendar events for a specific month and year grid view.
    /// </summary>
    /// <param name="year">The target year.</param>
    /// <param name="month">The target month (1-12).</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A MonthViewDto organizing events by days.</returns>
    [HttpGet("events")]
    public async Task<IActionResult> GetEventsForMonth([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
        {
            return BadRequest("Invalid year or month.");
        }

        // We want a full grid view, meaning we probably need a few days from previous/next month
        // to fill out the 6-week display block. Let's get the start/end of the requested month,
        // then pad out.
        var firstDayOfMonth = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var startDay = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek); // Back to Sunday
        
        var nextMonth = firstDayOfMonth.AddMonths(1);
        var endDay = nextMonth.AddDays(7 - (int)Math.Max(1, (int)nextMonth.DayOfWeek)); // Pad to Saturday (approx)
        
        // Add robust padding (roughly 2 months window total to be safe)
        var events = await _calendarRepository.GetEventsAsync(
            start: startDay, 
            end: endDay.AddDays(14), // Cover 6 grid rows
            ct: ct);

        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendarDict = calendars.ToDictionary(c => c.Id, c => c);

        var monthView = new MonthViewDto
        {
            Year = year,
            Month = month
        };

        foreach (var evt in events)
        {
            var vm = new CalendarEventViewModel
            {
                Id = evt.Id.ToString(),
                Title = evt.Title,
                StartTime = evt.Start.LocalDateTime,
                EndTime = evt.End.LocalDateTime,
                IsAllDay = evt.IsAllDay,
                Location = evt.Location,
                CalendarId = evt.CalendarInfoId
            };

            if (calendarDict.TryGetValue(evt.CalendarInfoId, out var cal))
            {
                vm.CalendarColor = cal.Color;
                vm.CalendarName = cal.DisplayName;
            }

            // An event might span multiple days. For this simple MVP, we just add it to its Start date.
            // A robust PWA would inject it across all days it spans in the Dictionary.
            var dateKey = evt.Start.ToString("yyyy-MM-dd");
            
            if (!monthView.Days.ContainsKey(dateKey))
            {
                monthView.Days[dateKey] = new List<CalendarEventViewModel>();
            }
            
            monthView.Days[dateKey].Add(vm);
        }

        return Ok(monthView);
    }

    /// <summary>
    /// Creates a new calendar event.
    /// </summary>
    /// <param name="calendarId">The target calendar internal Guid.</param>
    /// <param name="request">The creation payload.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created event ViewModel.</returns>
    [HttpPost("{calendarId:guid}/events")]
    public async Task<IActionResult> CreateEvent(Guid calendarId, [FromBody] CreateEventRequest request, CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var calendar = calendars.FirstOrDefault(c => c.Id == calendarId);
        if (calendar == null) return NotFound("Calendar not found.");

        var newEvent = new FamilyHQ.Core.Models.CalendarEvent
        {
            CalendarInfoId = calendarId,
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
            createdEvent.CalendarInfoId = calendarId;
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
                CalendarId = calendarId
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
    public async Task<IActionResult> UpdateEvent(Guid calendarId, Guid eventId, [FromBody] UpdateEventRequest request, CancellationToken ct)
    {
        var existing = await _calendarRepository.GetEventAsync(eventId, ct);
        if (existing == null || existing.CalendarInfoId != calendarId) return NotFound("Event not found.");

        existing.Title = request.Title;
        existing.Start = request.Start;
        existing.End = request.End;
        existing.IsAllDay = request.IsAllDay;
        existing.Location = request.Location;
        existing.Description = request.Description;

        try
        {
            var updatedGoogleEvent = await _googleCalendarClient.UpdateEventAsync(existing.CalendarInfo.GoogleCalendarId, existing, ct);
            
            // Sync up any potential Google-side changes (like updated tokens or IDs if any, although unlikely for an update)
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
                CalendarName = existing.CalendarInfo.DisplayName,
                CalendarColor = existing.CalendarInfo.Color,
                CalendarId = existing.CalendarInfoId
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
        if (existing == null || existing.CalendarInfoId != calendarId) return NotFound("Event not found.");

        try
        {
            await _googleCalendarClient.DeleteEventAsync(existing.CalendarInfo.GoogleCalendarId, existing.GoogleEventId, ct);
            await _calendarRepository.DeleteEventAsync(eventId, ct);
            await _calendarRepository.SaveChangesAsync(ct);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting event");
            return StatusCode(500, "Error communicating with Google Calendar");
        }
    }
}
