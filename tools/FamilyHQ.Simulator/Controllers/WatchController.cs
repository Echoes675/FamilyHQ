using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class WatchController : ControllerBase
{
    private readonly SimContext _db;
    private readonly ILogger<WatchController> _logger;

    public WatchController(SimContext db, ILogger<WatchController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("calendars/{calendarId}/events/watch")]
    public async Task<IActionResult> Watch(string calendarId, [FromBody] WatchRequest body)
    {
        if (body.Type != "web_hook")
        {
            _logger.LogWarning("[SIM] Watch request with unsupported type: {Type}", body.Type);
            return BadRequest("Only type 'web_hook' is supported.");
        }

        var resourceId = $"sim_resource_{Guid.NewGuid()}";
        var expiresAt = DateTimeOffset.UtcNow.AddDays(14);
        var expirationMs = expiresAt.ToUnixTimeMilliseconds();

        var channel = new SimulatedWatchChannel
        {
            CalendarId = calendarId,
            ChannelId = body.Id,
            Address = body.Address,
            ResourceId = resourceId,
            ExpiresAt = expiresAt
        };

        _db.WatchChannels.Add(channel);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "[SIM] Watch channel created: ChannelId={ChannelId}, CalendarId={CalendarId}, Address={Address}",
            body.Id, calendarId, body.Address);

        return Ok(new
        {
            kind = "api#channel",
            id = body.Id,
            resourceId,
            expiration = expirationMs
        });
    }

    [HttpPost("channels/stop")]
    public async Task<IActionResult> Stop([FromBody] StopRequest body)
    {
        var channel = await _db.WatchChannels
            .FirstOrDefaultAsync(wc => wc.ChannelId == body.Id && wc.ResourceId == body.ResourceId);

        if (channel != null)
        {
            _db.WatchChannels.Remove(channel);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SIM] Watch channel stopped: ChannelId={ChannelId}", body.Id);
        }
        else
        {
            _logger.LogWarning("[SIM] Watch channel not found for stop: ChannelId={ChannelId}, ResourceId={ResourceId}", body.Id, body.ResourceId);
        }

        return NoContent();
    }

    public record WatchRequest(string Id, string Type, string Address);
    public record StopRequest(string Id, string ResourceId);
}
