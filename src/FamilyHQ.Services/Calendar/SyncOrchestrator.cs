using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Calendar;

public class SyncOrchestrator : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISyncJobSignal _signal;
    private readonly ILogger<SyncOrchestrator> _logger;
    private readonly TimeSpan _periodicSyncInterval;

    public SyncOrchestrator(IServiceScopeFactory scopeFactory, ISyncJobSignal signal, ILogger<SyncOrchestrator> logger, IOptions<SyncOptions> options)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
        _periodicSyncInterval = options.Value.PeriodicSyncInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncOrchestrator started; periodic sync enqueues jobs for the CalendarSyncWorker.");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_periodicSyncInterval, stoppingToken);

            // Safety net: enqueue a sync-all job per registered user (mirrors the webhook fallback).
            // The CalendarSyncWorker drains each job with BackgroundUserContext set, CancellationToken.None,
            // and broadcast-after-persist — so the periodic path no longer runs with no user context (FHQ-38).
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tokenStore = scope.ServiceProvider.GetRequiredService<ITokenStore>();
                var queue = scope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();

                var userIds = await tokenStore.GetAllUserIdsAsync(stoppingToken);
                var enqueued = 0;
                foreach (var userId in userIds)
                {
                    if (string.IsNullOrEmpty(userId))
                        continue;
                    // null calendar = sync-all; Periodic source. Coalesces against an existing Pending job.
                    await queue.EnqueueAsync(userId, null, SyncJobSource.Periodic, null, stoppingToken);
                    enqueued++;
                }

                if (enqueued > 0)
                {
                    _signal.Release();
                    _logger.LogInformation("Periodic sync: enqueued sync-all for {Count} user(s).", enqueued);
                }
                else
                {
                    _logger.LogInformation("Periodic sync: no registered users with tokens; nothing to enqueue.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic sync enqueue failed.");
            }
        }
    }
}
