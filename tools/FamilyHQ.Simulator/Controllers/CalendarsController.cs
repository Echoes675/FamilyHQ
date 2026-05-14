using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class CalendarsController : ControllerBase
{
    private readonly SimContext _db;
    private readonly SyncFailureModeStore _failureStore;

    public CalendarsController(SimContext db, SyncFailureModeStore failureStore)
    {
        _db = db;
        _failureStore = failureStore;
    }

    [HttpGet("/users/me/calendarList")]
    public async Task<IActionResult> GetCalendarList()
    {
        var userId = ExtractUserId(Request);

        if (TryInjectFailure(userId, out var failureResult))
            return failureResult!;
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

    private bool TryInjectFailure(string? userId, out IActionResult? result)
    {
        result = SyncFailureResponse.TryBuild(_failureStore.Get(userId ?? string.Empty));
        return result is not null;
    }
}
