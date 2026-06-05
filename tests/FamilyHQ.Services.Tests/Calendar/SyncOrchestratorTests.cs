using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Calendar;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FamilyHQ.Services.Tests.Calendar;

public class SyncOrchestratorTests
{
    [Fact]
    public async Task ExecuteAsync_EnqueuesPeriodicSyncPerUser_AndSignalsWorker()
    {
        var (tokenStore, queue, signal, sync, _, sut) = CreateSut();
        using var cts = new CancellationTokenSource();
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Active("u-1"), Active("u-2") });
        signal.Setup(s => s.Release()).Callback(() => cts.Cancel()); // break the loop after the batch signals

        await sut.RunExecuteAsync(cts.Token);

        queue.Verify(q => q.EnqueueAsync("u-1", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.EnqueueAsync("u-2", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()), Times.Once);
        signal.Verify(s => s.Release(), Times.Once);
        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoRegisteredUsers_DoesNotSignal()
    {
        var (tokenStore, queue, signal, _, _, sut) = CreateSut();
        using var cts = new CancellationTokenSource();
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(Array.Empty<UserAuthState>());

        await sut.RunExecuteAsync(cts.Token);

        queue.Verify(q => q.EnqueueAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        signal.Verify(s => s.Release(), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnqueueThrows_CatchesAndContinues()
    {
        var (tokenStore, queue, signal, _, _, sut) = CreateSut();
        using var cts = new CancellationTokenSource();
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Active("u-1") });
        queue.Setup(q => q.EnqueueAsync("u-1", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new InvalidOperationException("db down"));

        await sut.RunExecuteAsync(cts.Token); // must not throw out of the loop

        queue.Verify(q => q.EnqueueAsync("u-1", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsNeedsReauthUsers_EnqueuesOnlyActive_AndLogsSkipAtInformation()
    {
        // FHQ-56: a NeedsReauth account fails every refresh — it must be skipped from the scheduled
        // sync (no enqueue, no invalid_grant noise), while healthy accounts still sync.
        var (tokenStore, queue, signal, _, logger, sut) = CreateSut();
        using var cts = new CancellationTokenSource();
        tokenStore.Setup(t => t.GetAllUserAuthStatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Active("u-active"), NeedsReauth("u-stale") });
        signal.Setup(s => s.Release()).Callback(() => cts.Cancel());

        await sut.RunExecuteAsync(cts.Token);

        queue.Verify(q => q.EnqueueAsync("u-active", null, SyncJobSource.Periodic, null, It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.EnqueueAsync("u-stale", It.IsAny<Guid?>(), It.IsAny<SyncJobSource>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        signal.Verify(s => s.Release(), Times.Once);
        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Information && r.Message.Contains("u-stale") && r.Message.Contains("re-authentication"),
            "the skipped account must be logged at Information so it's visible but not alarming");
    }

    private static UserAuthState Active(string userId) => new(userId, TokenAuthStatus.Active);
    private static UserAuthState NeedsReauth(string userId) => new(userId, TokenAuthStatus.NeedsReauth);

    private static (Mock<ITokenStore> TokenStore, Mock<ICalendarSyncJobQueue> Queue, Mock<ISyncJobSignal> Signal, Mock<ICalendarSyncService> Sync, RecordingLogger<SyncOrchestrator> Logger, TestableSyncOrchestrator SystemUnderTest) CreateSut()
    {
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var scopeMock = new Mock<IServiceScope>();
        var tokenStoreMock = new Mock<ITokenStore>();
        var queueMock = new Mock<ICalendarSyncJobQueue>();
        var signalMock = new Mock<ISyncJobSignal>();
        var syncServiceMock = new Mock<ICalendarSyncService>();
        var logger = new RecordingLogger<SyncOrchestrator>();

        // The orchestrator resolves the SCOPED collaborators (token store, queue) from a per-iteration
        // scope; the singleton signal is constructor-injected (signalMock below).
        var scopeServiceProviderMock = new Mock<IServiceProvider>();
        scopeServiceProviderMock
            .Setup(x => x.GetService(typeof(ITokenStore)))
            .Returns(tokenStoreMock.Object);
        scopeServiceProviderMock
            .Setup(x => x.GetService(typeof(ICalendarSyncJobQueue)))
            .Returns(queueMock.Object);

        scopeMock.Setup(x => x.ServiceProvider).Returns(scopeServiceProviderMock.Object);
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var options = Microsoft.Extensions.Options.Options.Create(new SyncOptions { PeriodicSyncInterval = TimeSpan.FromSeconds(1) });
        var systemUnderTest = new TestableSyncOrchestrator(scopeFactoryMock.Object, signalMock.Object, logger, options);

        return (tokenStoreMock, queueMock, signalMock, syncServiceMock, logger, systemUnderTest);
    }
}
