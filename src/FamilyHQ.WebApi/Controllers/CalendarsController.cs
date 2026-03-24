using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
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
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(
        ICalendarRepository calendarRepository,
        ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all synced calendars associated with the authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var dtos = calendars.Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color));
        return Ok(dtos);
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

        var monthView = new MonthViewDto { Year = year, Month = month };

        foreach (var evt in events)
        {
            var dto = new CalendarEventDto(
                evt.Id,
                evt.GoogleEventId,
                evt.Title,
                evt.Start,
                evt.End,
                evt.IsAllDay,
                evt.Location,
                evt.Description,
                evt.Calendars.Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color)).ToList());

            // Index once for each day the event spans within the viewable range
            var current = evt.Start.Date;
            var last = evt.End.AddTicks(-1).Date; // Use ticks to handle midnight end times correctly
            
            // Safety: cap multi-day events to a reasonable range if misconfigured
            int daysProcessed = 0;
            while (current <= last && daysProcessed < 366)
            {
                var dateKey = current.ToString("yyyy-MM-dd");
                if (!monthView.Days.ContainsKey(dateKey))
                    monthView.Days[dateKey] = [];

                monthView.Days[dateKey].Add(dto);
                current = current.AddDays(1);
                daysProcessed++;
            }
        }

        return Ok(monthView);
    }
}
