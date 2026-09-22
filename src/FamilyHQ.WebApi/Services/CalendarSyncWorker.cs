namespace FamilyHQ.WebApi.Services;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
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

        // FHQ-205: jobs whose processing escaped ProcessJobAsync entirely during THIS drain cycle.
        // Claiming one marks it InProgress, so ClaimNextAsync should not hand it back; if it does,
        // something reset it and re-processing it immediately would spin the loop at full speed.
        var unrecoverableJobIds = new HashSet<Guid>();

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();

            var job = await queue.ClaimNextAsync(stoppingToken);
            if (job is null) break;

            if (!unrecoverableJobIds.Add(job.Id))
            {
                // Leave it claimed and end the cycle. The next poll interval is the backoff, and
                // orphan recovery returns the job to Pending if it really is stuck.
                logger.LogError("Sync job {JobId} was claimed twice in one drain cycle after failing to be "
                    + "recorded; ending the cycle rather than re-processing it.", job.Id);
                break;
            }

            // FHQ-65: each job gets a fresh CorrelationId so all its logs (sync, broadcast,
            // retry/reauth) group together in Seq.
            using (logger.BeginCorrelationScope())
            {
                try
                {
                    await ProcessJobAsync(scope, queue, job, opts, stoppingToken);
                    unrecoverableJobIds.Remove(job.Id);
                }
                // FHQ-205: one calendar must not stop every calendar. A job that escapes
                // ProcessJobAsync — including one whose failure could not even be recorded — is
                // logged and the drain moves on to the next job. Previously such an exception
                // escaped DrainAsync, was caught in ExecuteAsync as "drain cycle failed", and took
                // every remaining calendar down with it: that is what turned one broken calendar
                // into a five-hour total outage. Genuine cancellation still propagates, so shutdown
                // semantics are unchanged.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Sync job {JobId} could not be processed or recorded as failed; "
                        + "continuing with the next job.", job.Id);
                }
            }
        }
    }

    private async Task ProcessJobAsync(IServiceScope scope, ICalendarSyncJobQueue queue, CalendarSyncJob job, SyncOptions opts, CancellationToken stoppingToken)
    {
        BackgroundUserContext.Current = job.UserId;
        try
        {
            // Reconcile-only work type (FHQ-69): placement reconciler, no Google sync. The mapping
            // lives in SyncJobSourceExtensions.IsReconcileOnly, shared with the enqueue coalescing guard.
            if (job.Source.IsReconcileOnly())
            {
                var reconciler = scope.ServiceProvider.GetRequiredService<IPlacementReconciler>();
                var rStart = DateTimeOffset.UtcNow.AddDays(-30);
                var rEnd = DateTimeOffset.UtcNow.AddDays(365);
                var reconciled = await reconciler.ReconcileForUserAsync(rStart, rEnd, CancellationToken.None);
                await queue.CompleteAsync(job.Id, stoppingToken);
                if (reconciled) await hubContext.Clients.All.SendAsync("EventsUpdated", CancellationToken.None);
                return;
            }

            var sync = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
            var start = DateTimeOffset.UtcNow.AddDays(-30);
            var end = DateTimeOffset.UtcNow.AddDays(365);

            // CancellationToken.None: never abort a sync mid-write because of client/request lifetime (FHQ-36).
            var result = job.CalendarInfoId is Guid calendarId
                ? await sync.SyncAsync(calendarId, start, end, CancellationToken.None)
                : await sync.SyncAllAsync(start, end, CancellationToken.None);

            // FHQ-68: a sync that changed events may have brought in a multi-attendee event created directly in
            // Google on a personal calendar. Re-evaluate placement so such events migrate to the shared calendar
            // (and are written back to Google), exactly as the DesignationChange path does. Only when the sync
            // changed something, so unchanged periodic syncs stay cheap.
            var placementChanged = false;
            if (result.HadChanges)
            {
                // Run placement in a FRESH scope (new DbContext). The sync above inserts the events it
                // brought in via AddEventAsync, leaving them tracked in THIS scope's DbContext; the
                // reconciler re-loads each event (AsNoTracking) and Updates it during a migration, which
                // collides with the still-tracked inserted instance (an EF identity conflict that silently
                // fails the migration). A clean DbContext avoids the conflict. The background user context
                // (set above) flows to the new scope, so the reconciler resolves the same user (FHQ-68).
                using var placementScope = scopeFactory.CreateScope();
                var reconciler = placementScope.ServiceProvider.GetRequiredService<IPlacementReconciler>();
                placementChanged = await reconciler.ReconcileForUserAsync(start, end, CancellationToken.None);
            }

            await queue.CompleteAsync(job.Id, stoppingToken);

            // Broadcast only when the sync actually changed data, so no-op/echo syncs
            // don't trigger a kiosk refresh (FHQ-44).
            if (result.HadChanges || placementChanged)
                await hubContext.Clients.All.SendAsync("EventsUpdated", CancellationToken.None);
        }
        catch (GoogleReauthRequiredException ex)
        {
            // FHQ-205: recorded in a FRESH scope — see RecordFailureAsync.
            using var failureScope = scopeFactory.CreateScope();
            var tokenStore = failureScope.ServiceProvider.GetRequiredService<ITokenStore>();
            await tokenStore.MarkNeedsReauthAsync(job.UserId, ex.ErrorDescription, stoppingToken);
            await failureScope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>()
                .FailAsync(job.Id, $"Reauth required: {ex.ErrorDescription}", retryable: false, retryAfter: null, stoppingToken);
            logger.LogWarning("Sync job {JobId} for user {UserId} needs re-auth.", job.Id, job.UserId);
        }
        // FHQ-91: an HttpClient per-attempt timeout surfaces as TaskCanceledException wrapping
        // TimeoutException. Syncs run with CancellationToken.None, so that shape can only mean a
        // hung Google endpoint — route it through the retryable path instead of letting it escape
        // and stall the job InProgress until orphan recovery. Genuine cancellation still propagates.
        catch (Exception ex) when (ex is not OperationCanceledException
            || ex is TaskCanceledException { InnerException: TimeoutException })
        {
            var retryable = job.AttemptCount < opts.MaxSyncAttempts;
            // Cap the exponent so a misconfigured MaxSyncAttempts cannot overflow TimeSpan.FromSeconds.
            var cappedAttempt = Math.Min(job.AttemptCount, 20);
            TimeSpan? backoff = retryable
                ? TimeSpan.FromSeconds(Math.Pow(2, cappedAttempt) * opts.RetryBackoffBaseSeconds)
                : null;

            // FHQ-83: honor Google's Retry-After as a floor — never requeue sooner than the upstream asked,
            // while still growing on repeated failures.
            if (retryable && backoff is { } expBackoff
                && ex is GoogleApiException { RetryAfter: { } retryAfter } && retryAfter > expBackoff)
                backoff = retryAfter;

            await RecordFailureAsync(job.Id, ex.Message, retryable, backoff, stoppingToken);
            logger.LogWarning(ex, "Sync job {JobId} failed (attempt {Attempt}, retryable={Retryable}).", job.Id, job.AttemptCount, retryable);
        }
        finally
        {
            BackgroundUserContext.Current = null;
        }
    }

    /// <summary>Records a job failure in a fresh scope, so a poisoned change tracker cannot hide it.</summary>
    /// <remarks>
    /// FHQ-205: the sync that just threw may have left THIS scope's DbContext holding an entry that
    /// can never be saved — the incident was exactly that, a detached calendar whose owned
    /// collection had an unresolvable shadow key. Calling FailAsync on that same DbContext threw
    /// again, the exception escaped ProcessJobAsync and DrainAsync, and the job was never even
    /// marked failed. A clean DbContext records the failure regardless of what the sync left
    /// behind. Same reasoning as the placementScope block in ProcessJobAsync.
    /// </remarks>
    private async Task RecordFailureAsync(Guid jobId, string error, bool retryable, TimeSpan? retryAfter, CancellationToken stoppingToken)
    {
        using var failureScope = scopeFactory.CreateScope();
        var queue = failureScope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();
        await queue.FailAsync(jobId, error, retryable, retryAfter, stoppingToken);
    }
}
