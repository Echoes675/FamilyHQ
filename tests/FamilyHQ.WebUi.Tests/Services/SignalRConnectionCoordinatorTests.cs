using System.Diagnostics;
using FamilyHQ.WebUi.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace FamilyHQ.WebUi.Tests.Services;

// FHQ-125: the reconnect/restart decision logic is extracted from SignalRService
// (HubConnection is not unit-mockable) so the state machine — events in → log
// actions + indicator state + restart schedule out — is fully testable with
// FakeTimeProvider and a fake restart callback.
public class SignalRConnectionCoordinatorTests
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    // WaitUntilAsync bounds (real wall-clock, failure path only — see the helper).
    private static readonly TimeSpan ConditionDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void IsConnectionDown_BeforeAnyEvents_IsFalse()
    {
        // Arrange
        var sut = CreateSut(new FakeTimeProvider());

        // Assert
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public void OnStarted_LogsInformation_AndConnectionStaysUp()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(new FakeTimeProvider(), logger);

        // Act
        sut.OnStarted();

        // Assert
        sut.IsConnectionDown.Should().BeFalse();
        VerifyLog(logger, LogLevel.Information, Times.Once());
    }

    [Fact]
    public void OnStartFailed_LogsError_SetsConnectionDown_AndRaisesStateChanged()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(new FakeTimeProvider(), logger);
        var stateChanges = 0;
        sut.ConnectionStateChanged += () => stateChanges++;

        // Act
        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Assert
        sut.IsConnectionDown.Should().BeTrue();
        stateChanges.Should().Be(1);
        VerifyLog(logger, LogLevel.Error, Times.Once(),
            messageContains: "Initial SignalR connection failed", withException: true);
    }

    [Fact]
    public async Task OnStartFailed_SchedulesRestart_AfterInitialDelay()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        // Act
        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Assert — nothing fires before the initial delay elapses
        time.Advance(InitialDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await WaitUntilAsync(() => calls == 1);
    }

    [Fact]
    public async Task RestartSuccess_SetsConnectionUp_RaisesConnectionRestored_AndLogsInformation()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var logger = CreateLogger();
        var restored = 0;
        var sut = CreateSut(time, logger, restart: _ => Task.CompletedTask);
        sut.ConnectionRestored += () => restored++;

        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Act
        time.Advance(InitialDelay);
        await WaitUntilAsync(() => !sut.IsConnectionDown);

        // Assert
        restored.Should().Be(1);
        VerifyLog(logger, LogLevel.Information, Times.AtLeastOnce());
    }

    [Fact]
    public async Task RestartAuthFailure_LogsWarning_AndSchedulesAnotherAttempt()
    {
        // Arrange — the ticket's test case: an auth failure during restart must be
        // logged and another attempt scheduled, not swallowed. The outage itself is
        // already reported at Error by OnClosed; per-attempt failures during a known
        // outage are expected-and-handled → Warning (logging skill).
        var time = new FakeTimeProvider();
        var logger = CreateLogger();
        var calls = 0;
        var sut = CreateSut(time, logger, restart: _ =>
        {
            calls++;
            if (calls == 1)
            {
                throw new HttpRequestException(
                    "Response status code does not indicate success: 401 (Unauthorized).");
            }

            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act — first attempt fails
        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);

        // Assert — the outage is the only Error; the failed attempt logs a Warning
        // with the auth exception attached
        sut.IsConnectionDown.Should().BeTrue();
        VerifyLog(logger, LogLevel.Error, Times.Once(),
            messageContains: "closed permanently", withException: true);
        VerifyLog(logger, LogLevel.Warning, Times.Once(),
            messageContains: "restart attempt", withException: true);

        // Act — second attempt runs after the doubled delay and succeeds
        time.Advance(MaxDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await WaitUntilAsync(() => calls == 2);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task RestartDelays_AreCappedAtMaxRetryDelay()
    {
        // Arrange — delegate always fails; delays should follow 5s, 10s, 10s (capped), 10s...
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            throw new HttpRequestException("still unreachable");
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act / Assert — attempt 1 after the initial delay
        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);

        // Attempt 2 after the doubled delay
        time.Advance(MaxDelay);
        await WaitUntilAsync(() => calls == 2);

        // Attempt 3 would be 20s uncapped — must fire at the 10s cap
        time.Advance(MaxDelay);
        await WaitUntilAsync(() => calls == 3);

        // Attempt 4 stays at the cap: nothing just before it, fires at it
        time.Advance(MaxDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Should().Be(3);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await WaitUntilAsync(() => calls == 4);
    }

    [Fact]
    public void OnReconnecting_LogsWarning_AndSetsConnectionDown()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(new FakeTimeProvider(), logger);
        var stateChanges = 0;
        sut.ConnectionStateChanged += () => stateChanges++;

        // Act
        sut.OnReconnecting(new InvalidOperationException("transport dropped"));

        // Assert
        sut.IsConnectionDown.Should().BeTrue();
        stateChanges.Should().Be(1);
        VerifyLog(logger, LogLevel.Warning, Times.Once(),
            messageContains: "automatic reconnect in progress", withException: true);
    }

    [Fact]
    public void OnReconnecting_WhenAlreadyDown_DoesNotRaiseStateChangedAgain()
    {
        // Arrange
        var sut = CreateSut(new FakeTimeProvider());
        var stateChanges = 0;
        sut.ConnectionStateChanged += () => stateChanges++;

        // Act
        sut.OnReconnecting(new InvalidOperationException("transport dropped"));
        sut.OnReconnecting(null);

        // Assert
        stateChanges.Should().Be(1);
    }

    [Fact]
    public void OnReconnected_LogsInformation_SetsConnectionUp_AndRaisesConnectionRestored()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(new FakeTimeProvider(), logger);
        var restored = 0;
        sut.ConnectionRestored += () => restored++;
        sut.OnReconnecting(null);

        // Act
        sut.OnReconnected();

        // Assert
        sut.IsConnectionDown.Should().BeFalse();
        restored.Should().Be(1);
        VerifyLog(logger, LogLevel.Information, Times.Once());
    }

    [Fact]
    public async Task OnClosed_LogsError_SetsConnectionDown_AndSchedulesRestart()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var logger = CreateLogger();
        var calls = 0;
        var sut = CreateSut(time, logger, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        // Act
        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Assert
        sut.IsConnectionDown.Should().BeTrue();
        VerifyLog(logger, LogLevel.Error, Times.Once(),
            messageContains: "closed permanently", withException: true);

        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);
    }

    [Fact]
    public async Task OnReconnected_WhileRestartPending_CancelsScheduledRestart()
    {
        // Arrange — Closed starts the restart loop, then automatic reconnect wins the race
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act
        sut.OnReconnected();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Should().Be(0);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task OnStarted_WhileRestartPending_CancelsScheduledRestart()
    {
        // Arrange — a failed first start schedules restarts; a later manual
        // StartAsync (e.g. revisiting the dashboard) succeeds first.
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Act
        sut.OnStarted();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Should().Be(0);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task RestartSuccess_ResetsBackoff_ForSubsequentOutage()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("first outage"));
        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);

        // Act — a second outage must start again from the initial delay
        sut.OnClosed(new InvalidOperationException("second outage"));

        time.Advance(InitialDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await WaitUntilAsync(() => calls == 2);
    }

    [Fact]
    public async Task OnStartFailed_WhileRestartLoopRunning_DoesNotStartSecondLoop()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        // Act — two failure reports, but only one loop may run
        sut.OnStartFailed(new InvalidOperationException("first failure"));
        sut.OnStartFailed(new InvalidOperationException("second failure"));

        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);
        await YieldAsync();

        // Assert
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_CancelsPendingRestart()
    {
        // Arrange
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act
        sut.Dispose();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Should().Be(0);
    }

    [Fact]
    public async Task OnClosed_AfterCancelledRestartLoop_SchedulesRestartAgain()
    {
        // Arrange — a cancelled loop must never leave the coordinator believing a
        // loop is still running, or a later outage would go un-retried forever.
        var time = new FakeTimeProvider();
        var calls = 0;
        var sut = CreateSut(time, restart: _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("first outage"));
        sut.OnStarted(); // cancels the pending restart loop

        // Act — a fresh outage after the cancellation
        sut.OnClosed(new InvalidOperationException("second outage"));
        time.Advance(InitialDelay);
        await WaitUntilAsync(() => calls == 1);

        // Assert
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WithNonPositiveInitialRetryDelay_Throws(int seconds)
    {
        // Arrange — config-bound delays must fail fast at startup: zero would spin a
        // hot restart loop; negative would kill the loop with the indicator stuck on.
        var options = new SignalRReconnectOptions
        {
            InitialRetryDelay = TimeSpan.FromSeconds(seconds),
            MaxRetryDelay = MaxDelay
        };

        // Act
        var act = () => new SignalRConnectionCoordinator(
            CreateLogger().Object, new FakeTimeProvider(), options);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithMaxRetryDelayBelowInitial_Throws()
    {
        // Arrange
        var options = new SignalRReconnectOptions
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(5)
        };

        // Act
        var act = () => new SignalRConnectionCoordinator(
            CreateLogger().Object, new FakeTimeProvider(), options);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OnStartFailed_BeforeInitialize_Throws()
    {
        // Arrange — constructed directly so Initialize is never called
        var sut = new SignalRConnectionCoordinator(
            CreateLogger().Object, new FakeTimeProvider(), CreateOptions());

        // Act
        var act = () => sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Initialize_CalledTwice_Throws()
    {
        // Arrange
        var sut = CreateSut(new FakeTimeProvider());

        // Act
        var act = () => sut.Initialize(_ => Task.CompletedTask);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ---------- helpers ----------

    private static SignalRConnectionCoordinator CreateSut(
        FakeTimeProvider time,
        Mock<ILogger<SignalRConnectionCoordinator>>? logger = null,
        SignalRReconnectOptions? options = null,
        Func<CancellationToken, Task>? restart = null)
    {
        var sut = new SignalRConnectionCoordinator(
            (logger ?? CreateLogger()).Object,
            time,
            options ?? CreateOptions());
        sut.Initialize(restart ?? (_ => Task.CompletedTask));
        return sut;
    }

    private static Mock<ILogger<SignalRConnectionCoordinator>> CreateLogger() => new();

    private static SignalRReconnectOptions CreateOptions() => new()
    {
        InitialRetryDelay = InitialDelay,
        MaxRetryDelay = MaxDelay
    };

    private static void VerifyLog(
        Mock<ILogger<SignalRConnectionCoordinator>> logger,
        LogLevel level,
        Times times,
        string? messageContains = null,
        bool withException = false) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    messageContains == null || state.ToString()!.Contains(messageContains)),
                It.Is<Exception?>(e => !withException || e != null),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    /// <summary>
    /// Lets thread-pool continuations of an already-completed fake delay run.
    /// Uses only <see cref="Task.Yield"/> — no real timers or sleeps.
    /// </summary>
    private static async Task YieldAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }
    }

    /// <summary>
    /// Waits until the condition holds, then yields a little longer so the
    /// coordinator's synchronous continuation — e.g. scheduling the next backoff
    /// delay — completes too. Wall-clock-bounded: polls with a yield plus a small
    /// real delay between checks (the JwtRenewalServiceTests pattern) so the
    /// thread pool is guaranteed time to run the loop's queued continuation under
    /// CI load — a fixed yield budget burns scheduler round-trips, not time, and
    /// expired before the continuation ran (see intermittent-issues issue 10).
    /// The deadline only bounds the failure path; the normal case exits within
    /// the first checks in microseconds.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < ConditionDeadline)
        {
            await Task.Yield();
            if (condition())
            {
                break;
            }

            await Task.Delay(PollInterval);
        }

        condition().Should().BeTrue("the coordinator should have reached the expected state");
        await YieldAsync();
    }
}
