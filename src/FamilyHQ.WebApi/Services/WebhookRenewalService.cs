namespace FamilyHQ.WebApi.Services;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Keeps every calendar's Google watch channel alive. FHQ-213: this polls on a short fixed cadence and
/// lets <see cref="FamilyHQ.Services.Calendar.WebhookRegistrationService.RegisterForCalendarAsync"/>
/// decide what is due, rather than sleeping one channel-lifetime between passes.
/// </summary>
public class WebhookRenewalService(
    IServiceProvider serviceProvider,
    IOptions<SyncOptions> options,
    TimeProvider timeProvider,
    ILogger<WebhookRenewalService> logger) : BackgroundService
{
    /// <summary>Lets app startup finish before the first pass touches the database or Google.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled. WebhookRenewalService will not run.");
            return;
        }

        var pollInterval = options.Value.WebhookRenewalPollInterval;
        WarnIfTheLegacyIntervalIsConfigured(pollInterval);

        // Wait for app startup to complete before first registration
        await Task.Delay(StartupDelay, timeProvider, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // FHQ-65: fresh CorrelationId per renewal cycle.
            using (logger.BeginCorrelationScope())
            {
                try
                {
                    await RegisterAllWebhooksAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Webhook renewal pass failed. The next pass is in {PollInterval}.", pollInterval);
                }
            }

            // FHQ-213: this is a POLL cadence, deliberately far shorter than the 24-hour window
            // RegisterForCalendarAsync re-registers inside. It used to be WebhookRenewalInterval — six
            // days, measured from process start — while Google's ~7-day channel clock runs from
            // registration. A restart reset this timer and left Google's running, so the next pass could
            // fall days after the expiry it existed to prevent, and the pass at startup correctly
            // skipped because more than 24 hours remained. Polling frequently removes the coupling to
            // restart time entirely: a pass with nothing due costs one query per calendar and no
            // Google call.
            await Task.Delay(pollInterval, timeProvider, stoppingToken);
        }
    }

    /// <summary>
    /// FHQ-213. <c>Sync__WebhookRenewalInterval</c> is still set in the deployed env credentials and is
    /// now ignored — honouring it would reinstate the defect. Silently ignoring a key an operator
    /// believes is in effect is how the next one of these hides, so say it once at startup instead.
    /// </summary>
    private void WarnIfTheLegacyIntervalIsConfigured(TimeSpan pollInterval)
    {
        if (options.Value.WebhookRenewalInterval is not { } legacyInterval)
        {
            return;
        }

        logger.LogWarning(
            "Sync:WebhookRenewalInterval is configured as {LegacyInterval} but is no longer used (FHQ-213). " +
            "Renewal now polls every {PollInterval} and re-registers any channel inside its final 24 hours. " +
            "Remove the setting; Sync:WebhookRenewalPollInterval controls the cadence.",
            legacyInterval, pollInterval);
    }

    // Iterates users manually rather than calling WebhookRegistrationService.RenewAllAsync
    // because each user needs BackgroundUserContext set and a fresh DI scope so that
    // scoped services (ICalendarRepository, ICurrentUserService) resolve correctly.
    // Internal (not private) so unit tests can drive the loop directly, like CalendarSyncWorker.DrainAsync.
    internal async Task RegisterAllWebhooksAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var tokenStore = scope.ServiceProvider.GetRequiredService<ITokenStore>();
        var userIds = (await tokenStore.GetAllUserIdsAsync(ct)).ToList();

        // FHQ-213: Debug here, because the pass now reports its outcome in one Information summary
        // below rather than narrating its start. Kept at all only so a pass that hangs mid-flight leaves
        // a "started" marker when Debug is switched on to chase it.
        logger.LogDebug("Webhook renewal: processing {UserCount} user(s).", userIds.Count);

        var tally = WebhookRegistrationTally.None;

        foreach (var userId in userIds)
        {
            BackgroundUserContext.Current = userId;
            try
            {
                using var userScope = serviceProvider.CreateScope();
                var registrationService = userScope.ServiceProvider.GetRequiredService<IWebhookRegistrationService>();
                tally += await registrationService.RegisterAllAsync(userId, ct: ct);
            }
            catch (GoogleReauthRequiredException)
            {
                // FHQ-85: RegisterAllAsync already persisted NeedsReauth and Warning-logged the
                // detection — here it is a handled account-state condition, not an error. The
                // next cycle skips this user via the auth-status guard; continue with the rest.
                logger.LogInformation(
                    "Webhook renewal for {UserId} requires re-authentication; continuing with remaining users.",
                    userId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to renew webhooks for user {UserId}.", userId);
            }
            finally
            {
                BackgroundUserContext.Current = null;
            }
        }

        // FHQ-213: exactly ONE Information line per pass — the thing that proves the renewal loop is
        // alive. The per-calendar "still valid, skipping" lines it replaces were the only evidence of
        // liveness between the weekly re-registrations, and at hourly cadence keeping them would be
        // ~190 lines a day per environment. Demoting them alone was not an option either: no FamilyHQ
        // environment emits Debug (issue 14 in .agent/docs/intermittent-issues.md records the dead end
        // that cost), so Debug would have deleted the signal rather than quietened it — and a renewal
        // loop that silently stopped would look exactly like one working perfectly, which is the shape
        // of the bug this ticket fixes. A reader scanning Seq hourly can tell "alive, nothing due" from
        // "alive, renewed 7" from silence.
        logger.LogInformation(
            "Webhook renewal pass complete for {UserCount} user(s): {CalendarsChecked} calendar(s) checked, " +
            "{ChannelsRegistered} channel(s) re-registered, {ChannelsFoundExpired} found already expired. " +
            "Next pass in {PollInterval}.",
            userIds.Count,
            tally.CalendarsChecked,
            tally.ChannelsRegistered,
            tally.ChannelsFoundExpired,
            options.Value.WebhookRenewalPollInterval);
    }
}
