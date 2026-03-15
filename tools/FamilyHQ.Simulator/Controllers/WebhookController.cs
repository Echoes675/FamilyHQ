using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
[Route("simulate/push")]
public class WebhookController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> PushWebhook()
    {
        using var client = new HttpClient();
        var webApiUrl = "https://localhost:7196/api/sync/webhook";
        var webhookRequest = new HttpRequestMessage(HttpMethod.Post, webApiUrl);
        webhookRequest.Headers.Add("x-goog-resource-state", "sync");
        webhookRequest.Headers.Add("x-goog-resource-id", "simulated_resource_" + Guid.NewGuid().ToString());
        
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
