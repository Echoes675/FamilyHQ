using FamilyHQ.Simulator.State;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

/// <summary>
/// E2E backdoor endpoint that exposes outbound-write counts recorded by the
/// Simulator when the WebApi PUTs or POSTs events (simulating Google Calendar writes).
/// Used by WebhookEchoGuard E2E scenarios to assert exactly one outbound write
/// occurred for a given event and that no second write was triggered by the echo.
/// </summary>
[ApiController]
[Route("api/simulator/backdoor/write-counts")]
public class BackdoorWriteCountController : ControllerBase
{
    private readonly OutboundWriteCountStore _store;

    public BackdoorWriteCountController(OutboundWriteCountStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Returns the number of times the Simulator received a PUT or POST for the given event ID.
    /// </summary>
    [HttpGet("{eventId}")]
    public IActionResult GetCount(string eventId)
    {
        var count = _store.Get(eventId);
        return Ok(new { EventId = eventId, WriteCount = count });
    }

    /// <summary>
    /// Returns the total outbound writes recorded for a single (isolated test) user since the last
    /// reset. Used by the recurring-events echo-guard scenarios that create a series natively: the
    /// master's event ID is generated server-side, so the test asserts on its own user's total. A
    /// GLOBAL total would be contaminated by concurrent scenarios under the parallel E2E runner.
    /// </summary>
    [HttpGet("user/{userId}/total")]
    public IActionResult GetUserTotal(string userId)
    {
        var total = _store.TotalForUser(userId);
        return Ok(new { WriteCount = total });
    }

    /// <summary>
    /// Resets the per-user total for one (isolated test) user. Scenario AfterScenario hooks call this
    /// instead of a global reset: under the parallel E2E runner a global clear would wipe a concurrent
    /// scenario's counts mid-flight (the FHQ-31 ClearAll race). Per-event counts are unique-id keyed
    /// and need no reset.
    /// </summary>
    [HttpDelete("user/{userId}")]
    public IActionResult ResetUser(string userId)
    {
        _store.Reset(userId);
        return NoContent();
    }

    /// <summary>Resets ALL write counts. Not used by parallel scenario hooks; available for manual/dev use.</summary>
    [HttpDelete]
    public IActionResult ResetAll()
    {
        _store.ResetAll();
        return NoContent();
    }
}
