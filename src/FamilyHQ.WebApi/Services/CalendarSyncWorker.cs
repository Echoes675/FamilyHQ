namespace FamilyHQ.WebApi.Services;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Drains the durable CalendarSyncJob queue. Runs each sync with CancellationToken.None
/// (decoupled from any HTTP request) and broadcasts EventsUpdated only after the data is
/// persisted. Single sequential consumer.
/// </summary>
public class CalendarSyncWorker(
    IServiceScopeFactory scopeFactory,
    ISyncJobSignal signal,
    IHubContext<CalendarHub> hubContext,
    IOptions<SyncOptions> options,
    ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CalendarSyncWorker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(options.Value.WorkerPollInterval, stoppingToken);
                await DrainAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CalendarSyncWorker drain cycle failed.");
            }
        }
    }

    internal async Task DrainAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        using (var recoveryScope = scopeFactory.CreateScope())
        {
            var queue = recoveryScope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();
            await queue.RecoverOrphansAsync(opts.OrphanRecoveryThreshold, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();

            var job = await queue.ClaimNextAsync(stoppingToken);
            if (job is null) break;

            await ProcessJobAsync(scope, queue, job, opts, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(IServiceScope scope, ICalendarSyncJobQueue queue, CalendarSyncJob job, SyncOptions opts, CancellationToken stoppingToken)
    {
        BackgroundUserContext.Current = job.UserId;
        try
        {
            var sync = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
            var start = DateTimeOffset.UtcNow.AddDays(-30);
            var end = DateTimeOffset.UtcNow.AddDays(365);

            // CancellationToken.None: never abort a sync mid-write because of client/request lifetime (FHQ-36).
            var result = job.CalendarInfoId is Guid calendarId
                ? await sync.SyncAsync(calendarId, start, end, CancellationToken.None)
                : await sync.SyncAllAsync(start, end, CancellationToken.None);

            await queue.CompleteAsync(job.Id, stoppingToken);

            // Broadcast only when the sync actually changed data, so no-op/echo syncs
            // don't trigger a kiosk refresh (FHQ-44).
            if (result.HadChanges)
                await hubContext.Clients.All.SendAsync("EventsUpdated", CancellationToken.None);
        }
        catch (GoogleReauthRequiredException ex)
        {
            var tokenStore = scope.ServiceProvider.GetRequiredService<ITokenStore>();
            await tokenStore.MarkNeedsReauthAsync(job.UserId, ex.ErrorDescription, stoppingToken);
            await queue.FailAsync(job.Id, $"Reauth required: {ex.ErrorDescription}", retryable: false, retryAfter: null, stoppingToken);
            logger.LogWarning("Sync job {JobId} for user {UserId} needs re-auth.", job.Id, job.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var retryable = job.AttemptCount < opts.MaxSyncAttempts;
            // Cap the exponent so a misconfigured MaxSyncAttempts cannot overflow TimeSpan.FromSeconds.
            var cappedAttempt = Math.Min(job.AttemptCount, 20);
            TimeSpan? backoff = retryable
                ? TimeSpan.FromSeconds(Math.Pow(2, cappedAttempt) * opts.RetryBackoffBaseSeconds)
                : null;
            await queue.FailAsync(job.Id, ex.Message, retryable, backoff, stoppingToken);
            logger.LogWarning(ex, "Sync job {JobId} failed (attempt {Attempt}, retryable={Retryable}).", job.Id, job.AttemptCount, retryable);
        }
        finally
        {
            BackgroundUserContext.Current = null;
        }
    }
}
