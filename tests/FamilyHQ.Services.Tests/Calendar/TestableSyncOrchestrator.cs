using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Tests.Calendar;

// We subclass SyncOrchestrator to expose the protected ExecuteAsync method
internal class TestableSyncOrchestrator : SyncOrchestrator
{
    public TestableSyncOrchestrator(IServiceScopeFactory scopeFactory, ISyncJobSignal signal, ILogger<SyncOrchestrator> logger, IOptions<SyncOptions> options)
        : base(scopeFactory, signal, logger, options)
    {
    }

    public Task RunExecuteAsync(CancellationToken stoppingToken)
    {
        return base.ExecuteAsync(stoppingToken);
    }
}
