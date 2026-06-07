using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("api/simulator")]
public class SimulatorConfigController : ControllerBase
{
    private readonly SimContext _db;
    private readonly ILogger<SimulatorConfigController> _logger;

    public SimulatorConfigController(SimContext db, ILogger<SimulatorConfigController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("configure")]
    public async Task<IActionResult> Configure([FromBody] SimulatorConfigurationModel config)
    {
        if (config == null)
            return BadRequest("Configuration body is required.");

        // Remove only this user's existing data — other users' data is untouched
        var userCalendars = await _db.Calendars.Where(c => c.UserId == config.UserName).ToListAsync();
        var userEvents = await _db.Events.Where(e => e.UserId == config.UserName).ToListAsync();
        var userEventIds = userEvents.Select(e => e.Id).ToList();
        var userAttendees = await _db.EventAttendees.Where(a => userEventIds.Contains(a.EventId)).ToListAsync();

        _db.EventAttendees.RemoveRange(userAttendees);
        _db.Calendars.RemoveRange(userCalendars);
        _db.Events.RemoveRange(userEvents);

        // Upsert user record
        var existing = await _db.Users.FindAsync(config.UserName);
        if (existing == null)
            _db.Users.Add(new SimulatedUser { Id = config.UserName, Username = config.UserName });

        foreach (var c in config.Calendars)
        {
            _db.Calendars.Add(new SimulatedCalendar
            {
                Id = c.Id,
                Summary = c.Summary,
                BackgroundColor = c.BackgroundColor ?? "#9e9e9e",
                UserId = config.UserName
            });
        }

        foreach (var e in config.Events)
        {
            _db.Events.Add(new SimulatedEvent
            {
                Id = e.Id,
                CalendarId = e.CalendarId,
                Summary = e.Summary,
                Description = e.Description,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                IsAllDay = e.IsAllDay,
                UserId = config.UserName,
                RecurrenceRule = e.RecurrenceRule
            });

            foreach (var attendeeCalendarId in e.AttendeeCalendarIds)
            {
                _db.EventAttendees.Add(new SimulatedEventAttendee
                {
                    EventId = e.Id,
                    AttendeeCalendarId = attendeeCalendarId
                });
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation(
            "[SIM] Configured user {UserName} with {CalendarCount} calendars and {EventCount} events.",
            config.UserName, config.Calendars.Count, config.Events.Count);

        return Ok();
    }
}
