using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Tests.Calendar;

// We subclass SyncOrchestrator to expose the protected ExecuteAsync method
internal class TestableSyncOrchestrator : SyncOrchestrator
{
    public TestableSyncOrchestrator(IServiceProvider serviceProvider, ILogger<SyncOrchestrator> logger, IOptions<SyncOptions> options)
        : base(serviceProvider, logger, options)
    {
    }

    public Task RunExecuteAsync(CancellationToken stoppingToken)
    {
        return base.ExecuteAsync(stoppingToken);
    }
}
