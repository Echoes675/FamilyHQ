using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("api/simulator/backdoor/webhooks")]
public class BackdoorWebhookController : ControllerBase
{
    private readonly SimContext _db;

    public BackdoorWebhookController(SimContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns all registered watch channels for E2E test assertions.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var channels = await _db.WatchChannels.ToListAsync();
        return Ok(channels);
    }

    /// <summary>
    /// Removes all watch channels — used for E2E test cleanup.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAll()
    {
        _db.WatchChannels.RemoveRange(_db.WatchChannels);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
