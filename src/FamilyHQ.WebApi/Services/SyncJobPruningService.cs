namespace FamilyHQ.WebApi.Services;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Periodically deletes terminal (Completed/Failed) CalendarSyncJob rows older than
/// SyncOptions.TerminalJobRetention, so the table doesn't grow unbounded (FHQ-137).
/// </summary>
public class SyncJobPruningService(
    IServiceProvider serviceProvider,
    IOptions<SyncOptions> options,
    ILogger<SyncJobPruningService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // FHQ-137: fresh CorrelationId per pruning cycle.
            using (logger.BeginCorrelationScope())
            {
                try
                {
                    await PruneOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Sync job pruning cycle failed.");
                }
            }

            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }

    internal async Task PruneOnceAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<ICalendarSyncJobQueue>();

        var retention = options.Value.TerminalJobRetention;
        var pruned = await queue.PruneTerminalAsync(retention, ct);

        logger.LogInformation(
            "Sync job pruning: removed {Count} terminal job(s) older than {Retention}.", pruned, retention);
    }
}
