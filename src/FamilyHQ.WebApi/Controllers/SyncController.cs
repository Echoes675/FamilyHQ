using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private readonly ICalendarSyncJobQueue _syncJobQueue;
    private readonly ISyncJobSignal _syncJobSignal;

    public SyncController(
        ICalendarSyncService syncService,
        IHubContext<CalendarHub> hubContext,
        ITokenStore tokenStore,
        IServiceScopeFactory scopeFactory,
        ILogger<SyncController> logger,
        IWebhookRegistrationRepository webhookRegistrationRepo,
        ICalendarRepository calendarRepo,
        ICurrentUserService currentUser,
        IWebhookRegistrationService webhookRegistrationService,
        ICalendarSyncJobQueue syncJobQueue,
        ISyncJobSignal syncJobSignal)
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
        _syncJobQueue = syncJobQueue;
        _syncJobSignal = syncJobSignal;
    }

    /// <summary>
    /// Manually triggers a full synchronization of all connected Google Calendars.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A status response.</returns>
    [Authorize]
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerSync(CancellationToken ct)
    {
        // FHQ-31: explicit UserId guard. [Authorize] above gates the request on
        // ASP.NET's authentication middleware; this null check additionally
        // refuses to enter SyncAllAsync if the authenticated principal lacks a
        // 'sub' claim. Without this guard, an authenticated-but-claimless
        // request would reach CalendarSyncService.SyncAllAsync, which would
        // silently log-and-return on null userId — turning a real auth bug
        // into a 200 OK with no work done. That silent-success path was the
        // root cause of the Deploy-Staging #110 flake: the diagnostics test
        // saw sync-response-status=200 and read the "Active" badge because
        // the user was never marked NeedsReauth.
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        _logger.LogInformation("Manual sync triggered via API for user {UserId}.", userId);

        var startDate = DateTimeOffset.UtcNow.AddDays(-30);
        var endDate = DateTimeOffset.UtcNow.AddDays(365);

        try
        {
            await _syncService.SyncAllAsync(startDate, endDate, ct);
        }
        catch (GoogleReauthRequiredException ex)
        {
            _logger.LogWarning(
                "Manual sync rejected: Google re-authentication required ({Source}). {Description}",
                ex.FailureSource, ex.ErrorDescription);
            return Conflict(new
            {
                status = "needs_reauth",
                source = ex.FailureSource == GoogleAuthFailureSource.TokenRefresh ? "token_refresh" : "calendar_api",
                message = ex.ErrorDescription ?? "Google connection requires re-consent.",
                reconnectUrl = "/api/auth/login"
            });
        }
        catch (GoogleApiException ex)
        {
            _logger.LogError(
                "Manual sync failed: upstream Google API error {Status} ({Operation}).",
                (int)ex.StatusCode, ex.Operation);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                status = "upstream_error",
                message = $"Google API {ex.Operation} returned {(int)ex.StatusCode}."
            });
        }

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

        // FHQ-37: enqueue a durable job and ack immediately. We must NOT run the sync
        // inline on the request thread — doing so under the request CancellationToken
        // was the root cause of FHQ-36 (work cancelled when the request completed/aborted).
        // A 200 ack stops Google's retries; the durable queue + periodic safety net
        // guarantee the work actually runs.
        try
        {
            var enqueuedAny = false;

            // Attempt a targeted enqueue via channel-id lookup.
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
                        await _syncJobQueue.EnqueueAsync(
                            calendarInfo.UserId, registration.CalendarInfoId, SyncJobSource.Webhook, channelId, ct);
                        _logger.LogInformation(
                            "Enqueued targeted sync job for calendar {CalendarInfoId} (user {UserId}) via channel {ChannelId}.",
                            registration.CalendarInfoId, calendarInfo.UserId, channelId);
                        enqueuedAny = true;
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

            // Fall back to a periodic sync-all enqueue per user if no targeted job was enqueued.
            if (!enqueuedAny)
            {
                var userIds = (await _tokenStore.GetAllUserIdsAsync(ct)).ToList();
                _logger.LogInformation("Webhook fallback: enqueuing sync-all for {Count} user(s).", userIds.Count);

                foreach (var userId in userIds)
                {
                    await _syncJobQueue.EnqueueAsync(userId, null, SyncJobSource.Periodic, null, ct);
                }
            }

            // Wake the worker so the job is drained immediately rather than at the next poll.
            _syncJobSignal.Release();
        }
        catch (Exception ex)
        {
            // Never fail the ack: a 200 stops Google's retries and the periodic safety net reconciles.
            _logger.LogError(ex, "Error enqueuing webhook sync job(s).");
        }

        return Ok();
    }
}
