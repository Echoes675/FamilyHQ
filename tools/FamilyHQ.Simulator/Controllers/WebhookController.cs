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
    private readonly IHttpClientFactory _httpClientFactory;

    public WebhookController(IConfiguration configuration, SimContext db, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost]
    public async Task<IActionResult> PushWebhook([FromQuery] string? calendarId = null)
    {
        var client = _httpClientFactory.CreateClient();
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

            // FHQ-101: the WebApi acks every legitimate push with a 2xx (even an unknown channel),
            // so a non-2xx means the push did not land — a 429 from the webhook rate limiter, say.
            // Reporting OK regardless would leave an E2E scenario to fail ~30s later as an
            // unrelated "event never appeared" timeout; propagate the status so the push step is
            // the thing that fails.
            if (!result.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)result.StatusCode,
                    $"Webhook to {webApiUrl} was rejected. Status: {result.StatusCode}");
            }

            return Ok($"Webhook sent to {webApiUrl}. Status: {result.StatusCode}");
        }
        catch (Exception ex)
        {
            return Problem($"Failed to send webhook: {ex.Message}");
        }
    }
}
