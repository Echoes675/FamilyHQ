using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Tests.Calendar;

// We subclass SyncOrchestrator to expose the protected ExecuteAsync method
internal class TestableSyncOrchestrator : SyncOrchestrator
{
    public TestableSyncOrchestrator(IServiceProvider serviceProvider, ILogger<SyncOrchestrator> logger) 
        : base(serviceProvider, logger)
    {
    }

    public Task RunExecuteAsync(CancellationToken stoppingToken)
    {
        return base.ExecuteAsync(stoppingToken);
    }
}
