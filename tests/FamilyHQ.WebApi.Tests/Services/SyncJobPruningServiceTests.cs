using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

public class SyncJobPruningServiceTests
{
    private static (SyncJobPruningService service, Mock<ICalendarSyncJobQueue> queue) CreateSut(TimeSpan retention)
    {
        var queue = new Mock<ICalendarSyncJobQueue>();

        var services = new ServiceCollection();
        services.AddScoped(_ => queue.Object);
        var provider = services.BuildServiceProvider();

        var options = Options.Create(new SyncOptions { TerminalJobRetention = retention });

        var service = new SyncJobPruningService(provider, options, NullLogger<SyncJobPruningService>.Instance);

        return (service, queue);
    }

    [Fact]
    public async Task PruneOnceAsync_CallsPruneTerminalAsync_WithConfiguredRetention()
    {
        var (service, queue) = CreateSut(TimeSpan.FromDays(14));
        queue.Setup(q => q.PruneTerminalAsync(TimeSpan.FromDays(14), It.IsAny<CancellationToken>()))
             .ReturnsAsync(3);

        await service.PruneOnceAsync(CancellationToken.None);

        queue.Verify(q => q.PruneTerminalAsync(TimeSpan.FromDays(14), It.IsAny<CancellationToken>()), Times.Once);
    }
}
