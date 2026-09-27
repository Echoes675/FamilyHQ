using System.Net;
using System.Security.Cryptography;
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
    TimeProvider timeProvider,
    ILogger<WebhookRegistrationService> logger) : IWebhookRegistrationService
{
    private const string WebhookPath = "/api/sync/webhook";

    /// <summary>
    /// FHQ-213. How long before expiry a channel becomes due for re-registration. Google's channels
    /// last ~7 days; renewing inside the final day leaves the notification stream untouched for the
    /// other six and still gives a full day of retries before push actually stops.
    /// <para>
    /// <see cref="SyncOptions.WebhookRenewalPollInterval"/> must be shorter than this, or a pass can
    /// step straight over the window and let the channel lapse — which is exactly what FHQ-213 was.
    /// </para>
    /// </summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromHours(24);

    public async Task RegisterForCalendarAsync(Guid calendarInfoId, string googleCalendarId, bool force = false, CancellationToken ct = default) =>
        await RegisterForCalendarCoreAsync(calendarInfoId, googleCalendarId, force, ct);

    /// <summary>
    /// FHQ-213. The registration path, reporting what it did. The public overload discards that,
    /// because only a scheduled pass has anything to say about it.
    /// </summary>
    private async Task<WebhookRegistrationTally> RegisterForCalendarCoreAsync(
        Guid calendarInfoId, string googleCalendarId, bool force, CancellationToken ct)
    {
        var syncOptions = options.Value;

        if (!syncOptions.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping calendar {CalendarInfoId}", calendarInfoId);
            return WebhookRegistrationTally.None;
        }

        if (string.IsNullOrEmpty(syncOptions.WebhookBaseUrl))
        {
            logger.LogWarning("WebhookBaseUrl is not configured, skipping webhook registration for calendar {CalendarInfoId}", calendarInfoId);
            return WebhookRegistrationTally.None;
        }

        var existing = await webhookRegistrationRepository.GetByCalendarIdAsync(calendarInfoId, ct);
        var webhookUrl = $"{syncOptions.WebhookBaseUrl.TrimEnd('/')}{WebhookPath}";
        var addressHash = WebhookAddress.Hash(webhookUrl);
        // FHQ-202: through the injected provider, so renewal timing is drivable from a test.
        var now = timeProvider.GetUtcNow();
        var foundExpired = 0;

        if (existing is not null && existing.ExpiresAt <= now)
        {
            foundExpired = 1;

            // FHQ-213: until now this state was indistinguishable from a quiet calendar — the channel
            // had already stopped delivering and nothing said so. Re-registering below restores push,
            // but the silent gap already happened, so it is a Warning rather than an Information.
            logger.LogWarning(
                "Webhook channel {ChannelId} for calendar {CalendarInfoId} expired at {ExpiresAt}; " +
                "push notifications have been missing since then. Re-registering now.",
                existing.ChannelId, calendarInfoId, existing.ExpiresAt);
        }
        else if (!force && existing is not null && existing.ExpiresAt > now + RenewalWindow)
        {
            // FHQ-196: a channel with days left is only worth keeping if it points at the address
            // we are configured for NOW. A null hash is a row written before this column existed
            // and counts as a mismatch — the alternative is trusting an address never recorded.
            if (string.Equals(existing.RegisteredAddressHash, addressHash, StringComparison.Ordinal))
            {
                // FHQ-213: Debug, because this is now the outcome of an hourly poll rather than a
                // once-per-six-days event — at Information it would be one line per calendar per hour of
                // pure "nothing to do", and flooding Seq is how a real signal gets missed. It is NOT
                // simply dropped: no FamilyHQ environment emits Debug, so the liveness evidence this line
                // used to carry moved into WebhookRenewalService's one-per-pass Information summary, which
                // counts it via the returned tally.
                logger.LogDebug(
                    "Webhook for calendar {CalendarInfoId} still valid until {ExpiresAt}, skipping registration",
                    calendarInfoId, existing.ExpiresAt);
                return new WebhookRegistrationTally(CalendarsChecked: 1, ChannelsRegistered: 0, ChannelsFoundExpired: 0);
            }

            // Masked: a RelayRobin address carries its route key in the path, and that key is the
            // credential authorising a notification POST to FamilyHQ.
            logger.LogInformation(
                "Webhook for calendar {CalendarInfoId} is registered for a different address; " +
                "re-registering against {WebhookAddress} before the channel expires at {ExpiresAt}.",
                calendarInfoId, WebhookAddress.Mask(webhookUrl), existing.ExpiresAt);
        }

        try
        {
            var channelId = Guid.NewGuid().ToString();
            var channelToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            var response = await googleCalendarClient.WatchEventsAsync(googleCalendarId, channelId, webhookUrl, channelToken, ct);

            var registration = new WebhookRegistration
            {
                CalendarInfoId = calendarInfoId,
                ChannelId = response.ChannelId,
                ResourceId = response.ResourceId,
                ChannelToken = channelToken,
                RegisteredAddressHash = addressHash,
                ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(response.Expiration),
                RegisteredAt = timeProvider.GetUtcNow()
            };

            await webhookRegistrationRepository.UpsertAsync(registration, ct);

            logger.LogInformation(
                "Registered webhook for calendar {CalendarInfoId} with channel {ChannelId}, expires at {ExpiresAt}",
                calendarInfoId, response.ChannelId, registration.ExpiresAt);

            if (existing is not null)
            {
                try
                {
                    await googleCalendarClient.StopChannelAsync(existing.ChannelId, existing.ResourceId, ct);
                    logger.LogInformation(
                        "Stopped previous webhook channel {ChannelId} for calendar {CalendarInfoId}.",
                        existing.ChannelId, calendarInfoId);
                }
                catch (GoogleApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogInformation(
                        "Previous webhook channel {ChannelId} for calendar {CalendarInfoId} already expired; nothing to stop.",
                        existing.ChannelId, calendarInfoId);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to stop previous webhook channel {ChannelId} for calendar {CalendarInfoId}; it will expire naturally.",
                        existing.ChannelId, calendarInfoId);
                }
            }

            return new WebhookRegistrationTally(CalendarsChecked: 1, ChannelsRegistered: 1, foundExpired);
        }
        catch (WebhookNotSupportedException ex)
        {
            logger.LogInformation(
                "Calendar {CalendarInfoId} does not support push notifications ({Reason}); skipping webhook.",
                calendarInfoId, ex.Reason);
            await calendarRepository.MarkWebhooksUnsupportedAsync(calendarInfoId, ct);
            return new WebhookRegistrationTally(CalendarsChecked: 1, ChannelsRegistered: 0, foundExpired);
        }
        catch (Exception ex) when (ex is not GoogleReauthRequiredException)
        {
            // FHQ-85: a reauth-required failure must propagate (so RegisterAllAsync can persist
            // NeedsReauth for the user); every other failure stays contained per-calendar.
            logger.LogError(ex, "Failed to register webhook for calendar {CalendarInfoId}", calendarInfoId);
            return new WebhookRegistrationTally(CalendarsChecked: 1, ChannelsRegistered: 0, foundExpired);
        }
    }

    public async Task<WebhookRegistrationTally> RegisterAllAsync(string userId, bool force = false, CancellationToken ct = default)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping RegisterAllAsync for user {UserId}", userId);
            return WebhookRegistrationTally.None;
        }

        // FHQ-58: guard direct callers — a NeedsReauth account can't refresh its token, so registering
        // its webhooks just produces invalid_grant noise. (Null-safe: GetAuthStatusAsync returns Active
        // for unknown users in production; treat any non-NeedsReauth result as eligible.)
        var authStatus = await tokenStore.GetAuthStatusAsync(userId, ct);
        if (authStatus is { Status: TokenAuthStatus.NeedsReauth })
        {
            logger.LogInformation("Skipping webhook registration for {UserId}: account needs re-authentication.", userId);
            return WebhookRegistrationTally.None;
        }

        var calendars = await calendarRepository.GetCalendarsByUserIdAsync(userId, ct);
        var tally = WebhookRegistrationTally.None;

        try
        {
            foreach (var calendar in calendars)
            {
                if (!calendar.WebhooksSupported)
                {
                    logger.LogDebug("Calendar {CalendarInfoId} marked as not supporting webhooks; skipping.", calendar.Id);
                    continue;
                }

                tally += await RegisterForCalendarCoreAsync(calendar.Id, calendar.GoogleCalendarId, force, ct);
            }
        }
        catch (GoogleReauthRequiredException ex)
        {
            // FHQ-85: first detection of a dead grant can happen HERE — persist it (idempotent)
            // so the renewal cycle skips this user and the kiosk banner appears, instead of
            // silently retrying the dead token forever. Remaining calendars share the same
            // OAuth token, so the loop is aborted. The exception still surfaces to the caller
            // (foreground register-webhooks → 409 via DomainExceptionHandler).
            // CancellationToken.None: once reauth is detected, a request abort must not cancel
            // the mark. A mark failure is logged but never replaces the original reauth
            // exception — the caller needs the 409/reconnect payload, not a 500.
            try
            {
                await tokenStore.MarkNeedsReauthAsync(userId, ex.ErrorDescription, CancellationToken.None);
            }
            catch (Exception markEx)
            {
                logger.LogError(markEx,
                    "Failed to persist NeedsReauth for user {UserId} during webhook registration; rethrowing the original reauth failure.",
                    userId);
            }
            logger.LogWarning(
                "Webhook registration for user {UserId} requires re-authentication; remaining calendars skipped.",
                userId);
            throw;
        }

        return tally;
    }

    public async Task<WebhookRegistrationTally> RenewAllAsync(CancellationToken ct = default)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled, skipping RenewAllAsync");
            return WebhookRegistrationTally.None;
        }

        var userStates = await tokenStore.GetAllUserAuthStatesAsync(ct);
        var tally = WebhookRegistrationTally.None;

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

            try
            {
                tally += await RegisterAllAsync(state.UserId, ct: ct);
            }
            catch (GoogleReauthRequiredException)
            {
                // FHQ-85: RegisterAllAsync already persisted NeedsReauth for this user; one
                // dead grant must not abort the renewal cycle for the remaining users.
                logger.LogInformation(
                    "Webhook renewal for {UserId} detected re-authentication is required; continuing with remaining users.",
                    state.UserId);
            }
        }

        return tally;
    }
}
