using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ICalendarSyncService _syncService;
    private readonly IHubContext<CalendarHub> _hubContext;
    private readonly ILogger<SyncController> _logger;

    public SyncController(
        ICalendarSyncService syncService,
        IHubContext<CalendarHub> hubContext,
        ILogger<SyncController> logger)
    {
        _syncService = syncService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerSync(CancellationToken ct)
    {
        _logger.LogInformation("Manual sync triggered via API.");
        
        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(365);
        
        await _syncService.SyncAllAsync(startDate, endDate, ct);
        
        // Notify all connected UI clients that events changed
        await _hubContext.Clients.All.SendAsync("EventsUpdated", ct);
        
        return Ok(new { Message = "Sync completed successfully." });
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> GooglePushWebhook(CancellationToken ct)
    {
        // Google sends headers indicating the resource that changed:
        // x-goog-resource-id, x-goog-resource-uri, x-goog-resource-state (e.g. "exists", "sync")
        
        if (Request.Headers.TryGetValue("x-goog-resource-state", out var state))
        {
            _logger.LogInformation("Received Google Push Webhook with state: {State}", state.ToString());
        }
        else
        {
            _logger.LogInformation("Received Sync trigger (e.g. from Simulator).");
        }

        // 1. Acknowledge immediately to Google (or Simulator) that we received it
        // We shouldn't await the sync inline if it takes a while, but for MVF1 it is okay
        // as we want to test the flow end-to-end. In prod: background task queue.
        
        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(365);
        
        try
        {
            await _syncService.SyncAllAsync(startDate, endDate, ct);
            
            // 2. Notify clients via SignalR
            await _hubContext.Clients.All.SendAsync("EventsUpdated", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook sync.");
        }

        return Ok();
    }
}
