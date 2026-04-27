using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
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

            var response = await googleCalendarClient.WatchEventsAsync(googleCalendarId, channelId, webhookUrl, ct);

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

        var calendars = await calendarRepository.GetCalendarsByUserIdAsync(userId, ct);

        foreach (var calendar in calendars)
        {
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

        var userIds = await tokenStore.GetAllUserIdsAsync(ct);

        foreach (var userId in userIds)
        {
            await RegisterAllAsync(userId, ct: ct);
        }
    }
}
