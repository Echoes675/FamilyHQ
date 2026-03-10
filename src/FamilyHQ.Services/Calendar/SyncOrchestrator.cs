using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FamilyHQ.Core.Interfaces;

namespace FamilyHQ.Services.Calendar;

public class SyncOrchestrator : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncOrchestrator> _logger;

    public SyncOrchestrator(IServiceProvider serviceProvider, ILogger<SyncOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncOrchestrator background service is starting. Sync will be triggered by login or webhook.");

        // No startup sync - we have no user context at startup.
        // Sync is triggered by the AuthController after login or by the SyncController on webhook.
        while (!stoppingToken.IsCancellationRequested)
        {
            // In a real app we might await a channel/queue for on-demand sync triggers,
            // or poll every X hours. We'll just sleep for 1 hour.
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            
            // Periodically sync again (incremental)
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
                
                // TODO: These should be configurable via a settings page in the UI
                var startDate = DateTimeOffset.UtcNow.AddDays(-30);
                var endDate = DateTimeOffset.UtcNow.AddDays(365);
                
                await syncService.SyncAllAsync(startDate, endDate, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic sync failed.");
            }
        }
    }
}
