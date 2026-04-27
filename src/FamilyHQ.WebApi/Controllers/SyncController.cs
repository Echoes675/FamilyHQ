using FamilyHQ.Core.Interfaces;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ICalendarSyncService _syncService;
    private readonly IHubContext<CalendarHub> _hubContext;
    private readonly ITokenStore _tokenStore;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncController> _logger;
    private readonly IWebhookRegistrationRepository _webhookRegistrationRepo;
    private readonly ICalendarRepository _calendarRepo;
    private readonly ICurrentUserService _currentUser;
    private readonly IWebhookRegistrationService _webhookRegistrationService;

    public SyncController(
        ICalendarSyncService syncService,
        IHubContext<CalendarHub> hubContext,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        ILogger<SyncController> logger,
        IWebhookRegistrationRepository webhookRegistrationRepo,
        ICalendarRepository calendarRepo,
        ICurrentUserService currentUser,
        IWebhookRegistrationService webhookRegistrationService)
    {
        _syncService = syncService;
        _hubContext = hubContext;
        _tokenStore = tokenStore;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _webhookRegistrationRepo = webhookRegistrationRepo;
        _calendarRepo = calendarRepo;
        _currentUser = currentUser;
        _webhookRegistrationService = webhookRegistrationService;
    }

    /// <summary>
    /// Manually triggers a full synchronization of all connected Google Calendars.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A status response.</returns>
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

    /// <summary>
    /// Force-registers webhook channels for all calendars belonging to the authenticated user.
    /// </summary>
    [Authorize]
    [HttpPost("register-webhooks")]
    public async Task<IActionResult> RegisterWebhooks(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        _logger.LogInformation("Manual webhook registration triggered by user {UserId}.", userId);
        await _webhookRegistrationService.RegisterAllAsync(userId, force: true, ct);

        return Ok(new { Message = "Webhook registration completed." });
    }

    /// <summary>
    /// Webhook endpoint intended for Google Calendar push notifications to trigger an incremental sync.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A 200 OK status.</returns>
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

        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(365);

        try
        {
            var synced = false;

            // Attempt targeted sync via channel-id lookup
            if (Request.Headers.TryGetValue("x-goog-channel-id", out var channelIdHeader))
            {
                var channelId = channelIdHeader.ToString();
                _logger.LogInformation("Webhook contains channel ID: {ChannelId}", channelId);

                var registration = await _webhookRegistrationRepo.GetByChannelIdAsync(channelId, ct);
                if (registration is not null)
                {
                    var calendarInfo = await _calendarRepo.GetCalendarByIdAsync(registration.CalendarInfoId, ct);
                    if (calendarInfo is not null)
                    {
                        _logger.LogInformation(
                            "Targeted sync for calendar {CalendarInfoId} (user {UserId}) via channel {ChannelId}.",
                            registration.CalendarInfoId, calendarInfo.UserId, channelId);

                        BackgroundUserContext.Current = calendarInfo.UserId;
                        try
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
                            await syncService.SyncAsync(registration.CalendarInfoId, startDate, endDate, ct);
                            synced = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during targeted sync for calendar {CalendarInfoId}.", registration.CalendarInfoId);
                        }
                        finally
                        {
                            BackgroundUserContext.Current = null;
                        }
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Webhook channel {ChannelId} maps to calendar {CalendarInfoId} but calendar not found. Falling back to sync-all.",
                            channelId, registration.CalendarInfoId);
                    }
                }
                else
                {
                    _logger.LogWarning("No webhook registration found for channel {ChannelId}. Falling back to sync-all.", channelId);
                }
            }

            // Fall back to sync-all if targeted sync was not performed
            if (!synced)
            {
                var userIds = (await _tokenStore.GetAllUserIdsAsync(ct)).ToList();
                _logger.LogInformation("Webhook sync: found {Count} registered user(s).", userIds.Count);

                foreach (var userId in userIds)
                {
                    BackgroundUserContext.Current = userId;
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
                        await syncService.SyncAllAsync(startDate, endDate, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing user {UserId} during webhook.", userId);
                    }
                    finally
                    {
                        BackgroundUserContext.Current = null;
                    }
                }
            }

            // Notify clients via SignalR
            await _hubContext.Clients.All.SendAsync("EventsUpdated", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook sync.");
        }

        return Ok();
    }
}
