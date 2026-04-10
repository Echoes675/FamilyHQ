using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class CalendarsController : ControllerBase
{
    private readonly SimContext _db;

    public CalendarsController(SimContext db)
    {
        _db = db;
    }

    [HttpGet("/users/me/calendarList")]
    public async Task<IActionResult> GetCalendarList()
    {
        var userId = ExtractUserId(Request);
        // Order by Id for a stable response.  Without an explicit OrderBy, Postgres
        // returns rows in heap order, which varies across test runs once rows are
        // deleted and reinserted in the same database (very common when many E2E
        // scenarios share one simulator container).  The downstream sync service
        // assigns sequential DisplayOrder values in the order we return calendars,
        // so a non-deterministic response here makes column-order assertions flaky.
        var calendars = await _db.Calendars
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var response = new
        {
            items = calendars.Select(c => new
            {
                id = c.Id,
                summary = c.Summary,
                backgroundColor = c.BackgroundColor
            })
        };

        return Ok(response);
    }

    // Token format: "simulated_{userId}_{nonce}"
    private static string? ExtractUserId(HttpRequest request)
    {
        var auth = request.Headers.Authorization.ToString();
        const string prefix = "Bearer simulated_";
        if (!auth.StartsWith(prefix)) return null;
        var token = auth[prefix.Length..];
        var idx = token.LastIndexOf('_');
        return idx > 0 ? token[..idx] : null;
    }
}
