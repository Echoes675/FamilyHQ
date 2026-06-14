using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Auth;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

public class CalendarSyncWorkerTests
{
    private static (CalendarSyncWorker worker, Mock<ICalendarSyncJobQueue> queue, Mock<ICalendarSyncService> sync, Mock<IClientProxy> proxy, Mock<ITokenStore> tokenStore)
        CreateSut(CalendarSyncJob? firstJob, Mock<IPlacementReconciler>? reconciler = null)
    {
        var queue = new Mock<ICalendarSyncJobQueue>();
        queue.SetupSequence(q => q.ClaimNextAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(firstJob)
             .ReturnsAsync((CalendarSyncJob?)null); // drain stops after one
        queue.Setup(q => q.RecoverOrphansAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sync = new Mock<ICalendarSyncService>();
        var tokenStore = new Mock<ITokenStore>();

        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(proxy.Object);
        var hub = new Mock<IHubContext<CalendarHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var placementReconciler = reconciler ?? new Mock<IPlacementReconciler>();

        var services = new ServiceCollection();
        services.AddScoped(_ => queue.Object);
        services.AddScoped(_ => sync.Object);
        services.AddScoped(_ => tokenStore.Object);
        services.AddScoped(_ => placementReconciler.Object);
        var provider = services.BuildServiceProvider();

        var signal = new Mock<ISyncJobSignal>();
        var options = Options.Create(new FamilyHQ.Services.Options.SyncOptions());

        var worker = new CalendarSyncWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            signal.Object, hub.Object, options, NullLogger<CalendarSyncWorker>.Instance);

        return (worker, queue, sync, proxy, tokenStore);
    }

    [Fact]
    public async Task DrainAsync_TargetedJob_SyncsThatCalendar_ThenCompletes_ThenBroadcasts()
    {
        var cal = Guid.NewGuid();
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = cal, Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, queue, sync, proxy, _) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(cal, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(1));

        await worker.DrainAsync(CancellationToken.None);

        sync.Verify(s => s.SyncAsync(cal, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), CancellationToken.None), Times.Once);
        queue.Verify(q => q.CompleteAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DrainAsync_NullCalendar_RunsSyncAll()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = null, Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, _, sync, _, _) = CreateSut(job);
        sync.Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(1));

        await worker.DrainAsync(CancellationToken.None);

        sync.Verify(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task DrainAsync_ReauthException_MarksNeedsReauth_AndFailsTerminally()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = Guid.NewGuid(), Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, queue, sync, proxy, tokenStore) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GoogleReauthRequiredException(GoogleAuthFailureSource.TokenRefresh, "reconsent"));

        await worker.DrainAsync(CancellationToken.None);

        tokenStore.Verify(t => t.MarkNeedsReauthAsync("u-1", It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        queue.Verify(q => q.FailAsync(job.Id, It.IsAny<string>(), false, null, It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DrainAsync_TransientException_BelowMaxAttempts_FailsRetryable()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = Guid.NewGuid(), Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, queue, sync, _, _) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));

        await worker.DrainAsync(CancellationToken.None);

        queue.Verify(q => q.FailAsync(job.Id, It.IsAny<string>(), true, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DrainAsync_TransientException_AtMaxAttempts_FailsTerminally()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = Guid.NewGuid(), Status = SyncJobStatus.InProgress, AttemptCount = 5 };
        var (worker, queue, sync, proxy, _) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("fatal"));

        await worker.DrainAsync(CancellationToken.None);

        // AttemptCount (5) == MaxSyncAttempts default (5) → not retryable → terminal fail, no backoff, no broadcast.
        queue.Verify(q => q.FailAsync(job.Id, It.IsAny<string>(), false, null, It.IsAny<CancellationToken>()), Times.Once);
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DrainAsync_NoMaterialChanges_DoesNotBroadcast()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = Guid.NewGuid(), Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, queue, sync, proxy, _) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(0));

        await worker.DrainAsync(CancellationToken.None);

        queue.Verify(q => q.CompleteAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once); // still completed
        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DrainAsync_WithMaterialChanges_Broadcasts()
    {
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = Guid.NewGuid(), Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, _, sync, proxy, _) = CreateSut(job);
        sync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(3));

        await worker.DrainAsync(CancellationToken.None);

        proxy.Verify(c => c.SendCoreAsync("EventsUpdated", Array.Empty<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessJob_NormalSyncWithChanges_RunsPlacementReconciler()
    {
        var reconciler = new Mock<IPlacementReconciler>();
        reconciler.Setup(r => r.ReconcileForUserAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = null, Source = SyncJobSource.Periodic, Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, _, sync, _, _) = CreateSut(job, reconciler: reconciler);
        sync.Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(1));

        await worker.DrainAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileForUserAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessJob_NormalSyncWithNoChanges_DoesNotRunPlacementReconciler()
    {
        var reconciler = new Mock<IPlacementReconciler>();
        var job = new CalendarSyncJob { Id = Guid.NewGuid(), UserId = "u-1", CalendarInfoId = null, Source = SyncJobSource.Periodic, Status = SyncJobStatus.InProgress, AttemptCount = 1 };
        var (worker, _, sync, _, _) = CreateSut(job, reconciler: reconciler);
        sync.Setup(s => s.SyncAllAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResult(0));

        await worker.DrainAsync(CancellationToken.None);

        reconciler.Verify(r => r.ReconcileForUserAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
