using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using FamilyHQ.WebApi.Hubs;
using FamilyHQ.WebApi.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FamilyHQ.WebApi.Tests.Services;

/// <summary>
/// FHQ-205 containment: one broken calendar must not stop every calendar.
/// </summary>
/// <remarks>
/// The P1 incident was a persistence bug, but what turned one failing calendar into a five-hour
/// total outage was the worker. The sync threw and left the scope's DbContext change tracker
/// holding an entry that could never be saved; <c>FailAsync</c> ran on that SAME scope and threw
/// too; the exception escaped <c>ProcessJobAsync</c>, escaped <c>DrainAsync</c>, and was caught in
/// <c>ExecuteAsync</c> as "drain cycle failed" — so the job was never even marked failed and every
/// remaining job in the cycle was abandoned.
/// <para>
/// These tests use per-scope fakes rather than one shared Moq object, because the behaviour under
/// test is precisely that the failure is recorded on a DIFFERENT scope from the one the sync
/// poisoned. A single shared mock cannot tell the two apart.
/// </para>
/// </remarks>
public class CalendarSyncWorkerContainmentTests
{
    /// <summary>The production message, so the fake fails the way the incident failed.</summary>
    private const string ShadowKeyFailure =
        "The value of shadow key property 'CalendarInfo.DefaultReminders#EventReminders.Overrides"
        + "#EventReminder.__synthesizedOrdinal' is unknown when attempting to save changes.";

    /// <summary>State shared by the fakes resolved from ONE scope.</summary>
    private sealed class ScopeState
    {
        /// <summary>Set when a sync run in this scope threw — the scope's DbContext is now unusable.</summary>
        public bool Poisoned { get; set; }
    }

    /// <summary>State shared across every scope — the "database" the fakes read and write.</summary>
    private sealed class QueueState
    {
        public Queue<CalendarSyncJob> Pending { get; } = new();
        public List<Guid> Claimed { get; } = [];
        public List<Guid> Completed { get; } = [];
        public List<Guid> Failed { get; } = [];
        public List<Guid> Synced { get; } = [];

        /// <summary>Job ids whose sync throws.</summary>
        public HashSet<Guid> FailingSyncs { get; } = [];

        /// <summary>When true, even a clean scope cannot record a failure (the database itself is down).</summary>
        public bool RecordingFailuresIsBroken { get; set; }

        /// <summary>When set, ClaimNextAsync hands this job back for ever — the hot-loop scenario.</summary>
        public CalendarSyncJob? AlwaysClaimable { get; set; }
    }

    private sealed class FakeQueue(QueueState queueState, ScopeState scopeState) : ICalendarSyncJobQueue
    {
        public Task<CalendarSyncJob?> ClaimNextAsync(CancellationToken ct = default)
        {
            if (queueState.AlwaysClaimable is { } sticky)
            {
                queueState.Claimed.Add(sticky.Id);
                return Task.FromResult<CalendarSyncJob?>(sticky);
            }

            if (queueState.Pending.Count == 0) return Task.FromResult<CalendarSyncJob?>(null);

            var job = queueState.Pending.Dequeue();
            queueState.Claimed.Add(job.Id);
            return Task.FromResult<CalendarSyncJob?>(job);
        }

        public Task CompleteAsync(Guid id, CancellationToken ct = default)
        {
            ThrowIfUnusable();
            queueState.Completed.Add(id);
            return Task.CompletedTask;
        }

        public Task FailAsync(Guid id, string error, bool retryable, TimeSpan? retryAfter, CancellationToken ct = default)
        {
            ThrowIfUnusable();
            queueState.Failed.Add(id);
            return Task.CompletedTask;
        }

        private void ThrowIfUnusable()
        {
            // A save on a scope whose change tracker the failed sync poisoned throws again — this
            // is what stopped the job from being marked failed in production.
            if (scopeState.Poisoned || queueState.RecordingFailuresIsBroken)
                throw new InvalidOperationException(ShadowKeyFailure);
        }

        public Task EnqueueAsync(string userId, Guid? calendarInfoId, SyncJobSource source, string? channelId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> RecoverOrphansAsync(TimeSpan olderThan, CancellationToken ct = default) => Task.FromResult(0);

        public Task<int> PruneTerminalAsync(TimeSpan olderThan, CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<CalendarSyncJob>> GetRecentFailuresAsync(string userId, int limit, TimeSpan maxAge, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CalendarSyncJob>>([]);

        public Task<int> GetActiveJobCountAsync(string userId, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeSyncService(QueueState queueState, ScopeState scopeState) : ICalendarSyncService
    {
        public Task<SyncResult> SyncAsync(Guid calendarInfoId, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
            => Run(calendarInfoId);

        public Task<SyncResult> SyncAllAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
            => Run(Guid.Empty);

        private Task<SyncResult> Run(Guid calendarInfoId)
        {
            if (queueState.FailingSyncs.Contains(calendarInfoId))
            {
                scopeState.Poisoned = true;
                throw new InvalidOperationException(ShadowKeyFailure);
            }

            queueState.Synced.Add(calendarInfoId);
            return Task.FromResult(new SyncResult(0));
        }
    }

    private static CalendarSyncWorker CreateWorker(QueueState queueState)
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(new Mock<IClientProxy>().Object);
        var hub = new Mock<IHubContext<CalendarHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var services = new ServiceCollection();
        services.AddSingleton(queueState);
        services.AddScoped<ScopeState>();
        services.AddScoped<ICalendarSyncJobQueue>(sp =>
            new FakeQueue(sp.GetRequiredService<QueueState>(), sp.GetRequiredService<ScopeState>()));
        services.AddScoped<ICalendarSyncService>(sp =>
            new FakeSyncService(sp.GetRequiredService<QueueState>(), sp.GetRequiredService<ScopeState>()));
        services.AddScoped(_ => new Mock<IPlacementReconciler>().Object);
        var provider = services.BuildServiceProvider();

        return new CalendarSyncWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Mock<ISyncJobSignal>().Object,
            hub.Object,
            Options.Create(new SyncOptions()),
            NullLogger<CalendarSyncWorker>.Instance);
    }

    private static CalendarSyncJob Job(Guid calendarInfoId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = "u-1",
        CalendarInfoId = calendarInfoId,
        Source = SyncJobSource.Periodic,
        Status = SyncJobStatus.InProgress,
        AttemptCount = 1
    };

    [Fact]
    public async Task DrainAsync_WhenTheFailedSyncPoisonsItsScope_StillRecordsTheJobAsFailed()
    {
        var queueState = new QueueState();
        var brokenCalendar = Guid.NewGuid();
        var job = Job(brokenCalendar);
        queueState.FailingSyncs.Add(brokenCalendar);
        queueState.Pending.Enqueue(job);

        await CreateWorker(queueState).DrainAsync(CancellationToken.None);

        queueState.Failed.Should().Contain(job.Id,
            "the failure must be recorded on a FRESH scope — recording it on the scope the sync " +
            "poisoned is what stopped the job from ever being marked failed");
    }

    [Fact]
    public async Task DrainAsync_WhenOneJobFails_StillProcessesTheNextJob()
    {
        var queueState = new QueueState();
        var brokenCalendar = Guid.NewGuid();
        var healthyCalendar = Guid.NewGuid();
        var brokenJob = Job(brokenCalendar);
        var healthyJob = Job(healthyCalendar);
        queueState.FailingSyncs.Add(brokenCalendar);
        queueState.Pending.Enqueue(brokenJob);
        queueState.Pending.Enqueue(healthyJob);

        await CreateWorker(queueState).DrainAsync(CancellationToken.None);

        queueState.Synced.Should().Contain(healthyCalendar,
            "one broken calendar must not stop the calendars behind it in the queue");
        queueState.Completed.Should().Contain(healthyJob.Id);
    }

    [Fact]
    public async Task DrainAsync_WhenTheFailureCannotBeRecordedAtAll_DoesNotAbandonTheRestOfTheCycle()
    {
        // The full production shape: the sync throws AND the failure cannot be written anywhere, so
        // the exception escapes ProcessJobAsync entirely. Before FHQ-205 that escaped DrainAsync
        // too and every remaining job was abandoned.
        var queueState = new QueueState { RecordingFailuresIsBroken = true };
        var brokenCalendar = Guid.NewGuid();
        var healthyCalendar = Guid.NewGuid();
        queueState.FailingSyncs.Add(brokenCalendar);
        queueState.Pending.Enqueue(Job(brokenCalendar));
        queueState.Pending.Enqueue(Job(healthyCalendar));

        var drain = async () => await CreateWorker(queueState).DrainAsync(CancellationToken.None);

        await drain.Should().NotThrowAsync(
            "an unrecordable job failure must be contained, not escalated into a failed drain cycle");
        queueState.Synced.Should().Contain(healthyCalendar);
    }

    [Fact]
    public async Task DrainAsync_WhenTheSameUnrecordableJobIsClaimedAgain_EndsTheCycleInsteadOfSpinning()
    {
        // Containment must not become a hot loop: if the queue keeps handing back a job whose
        // failure cannot be recorded, the drain ends and the poll interval provides the backoff.
        var queueState = new QueueState { RecordingFailuresIsBroken = true };
        var brokenCalendar = Guid.NewGuid();
        var stickyJob = Job(brokenCalendar);
        queueState.FailingSyncs.Add(brokenCalendar);
        queueState.AlwaysClaimable = stickyJob;

        await CreateWorker(queueState).DrainAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        queueState.Claimed.Should().HaveCount(2,
            "the job is processed once, and the second claim of the same id ends the drain cycle");
    }

    [Fact]
    public async Task DrainAsync_WhenTheSameJobIdIsClaimedAgainAfterSucceeding_StillProcessesIt()
    {
        // The hot-loop guard must only fire for jobs that actually escaped. A job that completed
        // normally is forgotten, so a legitimate re-claim (a coalesced re-enqueue reusing the row)
        // is processed rather than silently dropped.
        var queueState = new QueueState();
        var calendar = Guid.NewGuid();
        var job = Job(calendar);
        queueState.Pending.Enqueue(job);
        queueState.Pending.Enqueue(job);

        await CreateWorker(queueState).DrainAsync(CancellationToken.None);

        queueState.Completed.Should().HaveCount(2);
    }
}
