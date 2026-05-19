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
    /// Resets all write counts — call in AfterScenario hooks to avoid cross-scenario leakage.
    /// </summary>
    [HttpDelete]
    public IActionResult ResetAll()
    {
        _store.Reset();
        return NoContent();
    }
}
