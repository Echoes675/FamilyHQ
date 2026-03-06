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
    private readonly ILogger<CalendarsController> _logger;

    public CalendarsController(ICalendarRepository calendarRepository, ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        return Ok(calendars);
    }

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
                Location = evt.Location
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
}
