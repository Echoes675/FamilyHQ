namespace FamilyHQ.Simulator.Controllers;

using System.Text.Json;
using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// FHQ-207: lets an E2E scenario change a calendar's Google-side <c>defaultReminders</c> while the
/// app is running.
/// </summary>
/// <remarks>
/// Why this exists: CalendarSyncService stores a calendar's defaults at CREATION, so on a fresh CI
/// database the next sync finds them identical and RefreshCalendarDefaultsAsync early-returns
/// without writing. The FHQ-205 outage needed a WRITE, which only happens when Google's defaults
/// DIFFER from the stored ones. Changing them mid-run is the only way CI can reach that path.
/// </remarks>
[ApiController]
[Route("api/simulator/backdoor/calendars")]
public class BackdoorCalendarsController(SimContext db) : ControllerBase
{
    [HttpPut("{calendarId}/default-reminders")]
    public async Task<IActionResult> SetDefaultReminders(
        string calendarId,
        [FromBody] SetCalendarDefaultRemindersRequest request,
        CancellationToken ct)
    {
        var calendar = await db.Calendars.FirstOrDefaultAsync(c => c.Id == calendarId, ct);
        if (calendar is null)
            return NotFound($"No simulated calendar with id '{calendarId}'.");

        // Stored as the JSON text of a bare array, matching how Google serves defaultReminders on a
        // calendarList entry and how CalendarsController reads it back. Null clears it.
        calendar.DefaultRemindersJson = request.Overrides is null
            ? null
            : JsonSerializer.Serialize(request.Overrides);

        await db.SaveChangesAsync(ct);
        return Ok(new { calendarId, defaultReminders = request.Overrides });
    }
}
