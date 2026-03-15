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
        var calendars = await _db.Calendars
            .Where(c => c.UserId == userId)
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
