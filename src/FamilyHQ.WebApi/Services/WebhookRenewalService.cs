namespace FamilyHQ.WebApi.Services;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class WebhookRenewalService(
    IServiceProvider serviceProvider,
    IOptions<SyncOptions> options,
    ILogger<WebhookRenewalService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WebhookRegistrationEnabled)
        {
            logger.LogInformation("Webhook registration is disabled. WebhookRenewalService will not run.");
            return;
        }

        // Wait for app startup to complete before first registration
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegisterAllWebhooksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook renewal cycle failed.");
            }

            await Task.Delay(options.Value.WebhookRenewalInterval, stoppingToken);
        }
    }

    // Iterates users manually rather than calling WebhookRegistrationService.RenewAllAsync
    // because each user needs BackgroundUserContext set and a fresh DI scope so that
    // scoped services (ICalendarRepository, ICurrentUserService) resolve correctly.
    private async Task RegisterAllWebhooksAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var tokenStore = scope.ServiceProvider.GetRequiredService<ITokenStore>();
        var userIds = (await tokenStore.GetAllUserIdsAsync(ct)).ToList();

        logger.LogInformation("Webhook renewal: processing {UserCount} user(s).", userIds.Count);

        foreach (var userId in userIds)
        {
            BackgroundUserContext.Current = userId;
            try
            {
                using var userScope = serviceProvider.CreateScope();
                var registrationService = userScope.ServiceProvider.GetRequiredService<IWebhookRegistrationService>();
                await registrationService.RegisterAllAsync(userId, ct: ct);
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
    }
}
