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
        _logger.LogInformation("SyncOrchestrator background service is starting.");

        // Do a full sync on startup (window: -30 days to +365 days)
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<ICalendarSyncService>();
            
            var startDate = DateTimeOffset.UtcNow.AddDays(-30);
            var endDate = DateTimeOffset.UtcNow.AddDays(365);
            
            await syncService.SyncAllAsync(startDate, endDate, stoppingToken);
        }
        catch (Exception ex)
        {
            // It's possible the user hasn't authenticated yet or DB isn't ready.
            _logger.LogWarning(ex, "Failed to perform initial startup sync. This is normal if OAuth is not complete.");
        }

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
