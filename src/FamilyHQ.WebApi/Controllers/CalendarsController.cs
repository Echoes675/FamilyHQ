using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
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

    public CalendarsController(ICalendarRepository calendarRepository, ILogger<CalendarsController> logger)
    {
        _calendarRepository = calendarRepository;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCalendars(CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var dtos = calendars
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new EventCalendarDto(c.Id, c.DisplayName, c.Color, c.IsShared));
        return Ok(dtos);
    }

    [HttpGet("events")]
    public async Task<IActionResult> GetEventsForMonth([FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        if (year < 2000 || year > 2100 || month < 1 || month > 12)
            return BadRequest("Invalid year or month.");

        var firstDay = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var startDay = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        var nextMonth = firstDay.AddMonths(1);
        var endDay = nextMonth.AddDays(7 - (int)Math.Max(1, (int)nextMonth.DayOfWeek));

        // +14 days: pre-fetches the first two weeks of the next month for scroll-ahead display.
        var events = await _calendarRepository.GetEventsAsync(start: startDay, end: endDay.AddDays(14), ct: ct);
        var allCalendars = await _calendarRepository.GetCalendarsAsync(ct);
        // Compare by Id — Members are AsNoTracking instances, not the same object references.
        var visibleCalendarIds = allCalendars
            .Where(c => !c.IsShared && c.IsVisible)
            .Select(c => c.Id)
            .ToHashSet();

        var monthView = new MonthViewDto { Year = year, Month = month };

        foreach (var evt in events)
        {
            // Project event into each assigned visible member's lane
            var visibleMembers = evt.Members.Where(m => visibleCalendarIds.Contains(m.Id)).ToList();
            if (visibleMembers.Count == 0) continue;

            foreach (var member in visibleMembers)
            {
                var dto = new CalendarEventDto(
                    evt.Id,
                    evt.GoogleEventId,
                    evt.Title,
                    evt.Start,
                    evt.End,
                    evt.IsAllDay,
                    evt.Location,
                    StripMemberTag(evt.Description),
                    evt.Members.Select(m => new EventCalendarDto(m.Id, m.DisplayName, m.Color, m.IsShared)).ToList());

                var current = evt.Start.Date;
                var last    = evt.End.AddTicks(-1).Date;
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
        }

        return Ok(monthView);
    }

    // Strip [members:...] tag from the description returned to the UI
    private static string? StripMemberTag(string? description)
    {
        if (string.IsNullOrEmpty(description)) return null;
        var stripped = System.Text.RegularExpressions.Regex
            .Replace(description, @"\[members:\s*[^\]]*\]", string.Empty)
            .Trim();
        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }

    /// <summary>Updates visibility and shared designation for a calendar.</summary>
    [HttpPut("{id:guid}/settings")]
    public async Task<IActionResult> UpdateCalendarSettings(
        Guid id, [FromBody] CalendarSettingsRequest request, CancellationToken ct)
    {
        var calendar = await _calendarRepository.GetCalendarByIdAsync(id, ct);
        if (calendar == null) return NotFound();

        // Only one calendar can be shared at a time
        if (request.IsShared)
        {
            var currentShared = await _calendarRepository.GetSharedCalendarAsync(ct);
            if (currentShared != null && currentShared.Id != id)
            {
                currentShared.IsShared = false;
                await _calendarRepository.UpdateCalendarAsync(currentShared, ct);
            }
        }

        calendar.IsVisible = request.IsVisible;
        calendar.IsShared  = request.IsShared;
        await _calendarRepository.UpdateCalendarAsync(calendar, ct);
        await _calendarRepository.SaveChangesAsync(ct);

        return Ok(new EventCalendarDto(calendar.Id, calendar.DisplayName, calendar.Color, calendar.IsShared));
    }

    /// <summary>Saves the display order for all calendars (agenda column order).</summary>
    [HttpPut("order")]
    public async Task<IActionResult> SaveOrder([FromBody] CalendarOrderRequest request, CancellationToken ct)
    {
        var calendars = await _calendarRepository.GetCalendarsAsync(ct);
        var lookup    = calendars.ToDictionary(c => c.Id);

        foreach (var (calendarId, order) in request.Order)
        {
            if (!lookup.TryGetValue(calendarId, out var cal)) continue;
            cal.DisplayOrder = order;
            await _calendarRepository.UpdateCalendarAsync(cal, ct);
        }

        await _calendarRepository.SaveChangesAsync(ct);
        return NoContent();
    }
}
