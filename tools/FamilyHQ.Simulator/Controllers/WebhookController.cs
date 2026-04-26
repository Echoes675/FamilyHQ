using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("simulate/push")]
public class WebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly SimContext _db;

    public WebhookController(IConfiguration configuration, SimContext db)
    {
        _configuration = configuration;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> PushWebhook([FromQuery] string? calendarId = null)
    {
        using var client = new HttpClient();
        var webApiBaseUrl = _configuration["WebApiBaseUrl"] ?? "https://localhost:7196";
        var webApiUrl = webApiBaseUrl.TrimEnd('/') + "/api/sync/webhook";
        var webhookRequest = new HttpRequestMessage(HttpMethod.Post, webApiUrl);
        webhookRequest.Headers.Add("x-goog-resource-state", "sync");
        webhookRequest.Headers.Add("x-goog-resource-id", "simulated_resource_" + Guid.NewGuid().ToString());

        // Only include channel-id when a specific calendarId is requested,
        // so generic push notifications fall through to sync-all behaviour.
        if (calendarId is not null)
        {
            var storedChannel = await _db.WatchChannels
                .FirstOrDefaultAsync(c => c.CalendarId == calendarId);
            if (storedChannel != null)
            {
                webhookRequest.Headers.Add("x-goog-channel-id", storedChannel.ChannelId);
            }
        }

        try
        {
            var result = await client.SendAsync(webhookRequest);
            return Ok($"Webhook sent to {webApiUrl}. Status: {result.StatusCode}");
        }
        catch (Exception ex)
        {
            return Problem($"Failed to send webhook: {ex.Message}");
        }
    }
}
