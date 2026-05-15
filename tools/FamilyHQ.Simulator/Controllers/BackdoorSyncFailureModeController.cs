using FamilyHQ.Simulator.State;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

/// <summary>
/// Test back-door endpoint for injecting per-user sync failure modes.
/// Used exclusively by E2E tests; not part of the real Google API surface.
/// </summary>
[ApiController]
[Route("api/simulator/backdoor/sync-failure-mode")]
public class BackdoorSyncFailureModeController : ControllerBase
{
    private readonly SyncFailureModeStore _store;

    public BackdoorSyncFailureModeController(SyncFailureModeStore store)
    {
        _store = store;
    }

    public class SetFailureModeRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
    }

    [HttpPost]
    public IActionResult Set([FromBody] SetFailureModeRequest body)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.UserId))
            return BadRequest("UserId is required.");

        if (!Enum.TryParse<SyncFailureMode>(body.Mode, ignoreCase: true, out var mode))
            return BadRequest($"Unknown failure mode '{body.Mode}'.");

        _store.Set(body.UserId, mode);
        return Ok();
    }

    [HttpDelete]
    public IActionResult Clear([FromQuery] string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            _store.ClearAll();
        }
        else
        {
            _store.Clear(userId);
        }

        return NoContent();
    }
}
