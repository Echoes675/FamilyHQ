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
    /// Returns the total number of outbound writes recorded across all events since the last reset.
    /// Used by the recurring-events echo-guard scenarios that create a series natively: the master's
    /// event ID is generated server-side, so the test asserts on the total instead of a per-ID count.
    /// </summary>
    [HttpGet("total")]
    public IActionResult GetTotal()
    {
        var total = _store.Total();
        return Ok(new { WriteCount = total });
    }

    /// <summary>
    /// Resets all write counts — call in AfterScenario hooks to avoid cross-scenario leakage.
    /// </summary>
    [HttpDelete]
    public IActionResult ResetAll()
    {
        _store.Reset();
        return NoContent();
    }
}
