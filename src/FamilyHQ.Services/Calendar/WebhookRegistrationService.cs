using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

public class WebhookRegistrationService(
    IGoogleCalendarClient googleCalendarClient,
    IWebhookRegistrationRepository webhookRegistrationRepository,
    ICalendarRepository calendarRepository,
    ITokenStore tokenStore,
    IOptions<SyncOptions> options,
    ILogger<WebhookRegistrationService> logger) : IWebhookRegistrationService
{
    private const string WebhookPath = "/api/sync/webhook";

    public async Task RegisterForCalendarAsync(Guid calendarInfoId, string googleCalendarId, bool force = false, CancellationToken ct = default)
    {
        var syncOptions = options.Value;

        if (!syncOptions.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping calendar {CalendarInfoId}", calendarInfoId);
            return;
        }

        if (string.IsNullOrEmpty(syncOptions.WebhookBaseUrl))
        {
            logger.LogWarning("WebhookBaseUrl is not configured, skipping webhook registration for calendar {CalendarInfoId}", calendarInfoId);
            return;
        }

        if (!force)
        {
            var existing = await webhookRegistrationRepository.GetByCalendarIdAsync(calendarInfoId, ct);
            if (existing is not null && existing.ExpiresAt > DateTimeOffset.UtcNow.AddHours(24))
            {
                logger.LogInformation(
                    "Webhook for calendar {CalendarInfoId} still valid until {ExpiresAt}, skipping registration",
                    calendarInfoId, existing.ExpiresAt);
                return;
            }
        }

        try
        {
            var channelId = Guid.NewGuid().ToString();
            var webhookUrl = $"{syncOptions.WebhookBaseUrl.TrimEnd('/')}{WebhookPath}";

            var response = await googleCalendarClient.WatchEventsAsync(googleCalendarId, channelId, webhookUrl, string.Empty, ct);

            var registration = new WebhookRegistration
            {
                CalendarInfoId = calendarInfoId,
                ChannelId = response.ChannelId,
                ResourceId = response.ResourceId,
                ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(response.Expiration),
                RegisteredAt = DateTimeOffset.UtcNow
            };

            await webhookRegistrationRepository.UpsertAsync(registration, ct);

            logger.LogInformation(
                "Registered webhook for calendar {CalendarInfoId} with channel {ChannelId}, expires at {ExpiresAt}",
                calendarInfoId, response.ChannelId, registration.ExpiresAt);
        }
        catch (WebhookNotSupportedException ex)
        {
            logger.LogInformation(
                "Calendar {CalendarInfoId} does not support push notifications ({Reason}); skipping webhook.",
                calendarInfoId, ex.Reason);
            await calendarRepository.MarkWebhooksUnsupportedAsync(calendarInfoId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register webhook for calendar {CalendarInfoId}", calendarInfoId);
        }
    }

    public async Task RegisterAllAsync(string userId, bool force = false, CancellationToken ct = default)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping RegisterAllAsync for user {UserId}", userId);
            return;
        }

        // FHQ-58: guard direct callers — a NeedsReauth account can't refresh its token, so registering
        // its webhooks just produces invalid_grant noise. (Null-safe: GetAuthStatusAsync returns Active
        // for unknown users in production; treat any non-NeedsReauth result as eligible.)
        var authStatus = await tokenStore.GetAuthStatusAsync(userId, ct);
        if (authStatus is { Status: TokenAuthStatus.NeedsReauth })
        {
            logger.LogInformation("Skipping webhook registration for {UserId}: account needs re-authentication.", userId);
            return;
        }

        var calendars = await calendarRepository.GetCalendarsByUserIdAsync(userId, ct);

        foreach (var calendar in calendars)
        {
            if (!calendar.WebhooksSupported)
            {
                logger.LogDebug("Calendar {CalendarInfoId} marked as not supporting webhooks; skipping.", calendar.Id);
                continue;
            }

            await RegisterForCalendarAsync(calendar.Id, calendar.GoogleCalendarId, force, ct);
        }
    }

    public async Task RenewAllAsync(CancellationToken ct = default)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping RenewAllAsync");
            return;
        }

        var userStates = await tokenStore.GetAllUserAuthStatesAsync(ct);

        foreach (var state in userStates)
        {
            if (string.IsNullOrEmpty(state.UserId))
                continue;

            // FHQ-58: a NeedsReauth account fails every token refresh, so attempting webhook
            // registration is pure invalid_grant noise; the user is already prompted to reconnect.
            // Skip until re-consent flips AuthStatus back to Active (picked up next renewal cycle).
            if (state.AuthStatus == TokenAuthStatus.NeedsReauth)
            {
                logger.LogInformation("Skipping webhook registration for {UserId}: account needs re-authentication.", state.UserId);
                continue;
            }

            await RegisterAllAsync(state.UserId, ct: ct);
        }
    }
}
