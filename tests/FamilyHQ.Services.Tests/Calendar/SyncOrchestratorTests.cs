using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_PerformsInitialSync_AndRespectsCancellation()
    {
        // Arrange
        var (sync, systemUnderTest) = CreateSut();
        using var cts = new CancellationTokenSource();
        
        // We want to cancel immediately after the first sync completes so the while loop doesn't block forever
        sync.Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback(() => cts.Cancel()); // Auto-cancel to break the loop

        // Act
        // This will run the initial sync, hit the callback which cancels the token, then exit the while loop gracefully
        await systemUnderTest.RunExecuteAsync(cts.Token);

        // Assert
        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    
    [Fact]
    public async Task ExecuteAsync_WhenInitialSyncThrows_CatchesAndContinues()
    {
        // Arrange
        var (sync, systemUnderTest) = CreateSut();
        using var cts = new CancellationTokenSource();
        
        sync.Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OAuth not ready"))
            .Callback(() => cts.Cancel()); // Auto-cancel to break the loop

        // Act
        await systemUnderTest.RunExecuteAsync(cts.Token);

        // Assert
        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<ICalendarSyncService> Sync, TestableSyncOrchestrator SystemUnderTest) CreateSut()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var loggerMock = new Mock<ILogger<SyncOrchestrator>>();

        serviceProviderMock
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactoryMock.Object);

        scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        var scopeServiceProviderMock = new Mock<IServiceProvider>();
        scopeServiceProviderMock
            .Setup(x => x.GetService(typeof(ICalendarSyncService)))
            .Returns(syncServiceMock.Object);

        scopeMock.Setup(x => x.ServiceProvider).Returns(scopeServiceProviderMock.Object);

        var systemUnderTest = new TestableSyncOrchestrator(serviceProviderMock.Object, loggerMock.Object);

        return (syncServiceMock, systemUnderTest);
    }
}
