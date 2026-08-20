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
//
// FHQ-158: every advance of the fake clock goes through TimerArmedTimeProvider and every wait for
// the coordinator to act goes through AwaitableCounter. Neither burns yields nor sleeps: the clock
// is only advanced once the coordinator has provably armed the timer that advance is meant to fire,
// and a wait resumes from inside the coordinator's own continuation. Settling on a fixed yield
// budget instead lost advances under CI load and broke master build #59 on source identical to a
// green dev — see issue 10 in .agent/docs/intermittent-issues.md.
public class SignalRConnectionCoordinatorTests
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    [Fact]
    public void IsConnectionDown_BeforeAnyEvents_IsFalse()
    {
        // Arrange
        var sut = CreateSut(CreateClock());

        // Assert
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public void OnStarted_LogsInformation_AndConnectionStaysUp()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(CreateClock(), logger);

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
        var sut = CreateSut(CreateClock(), logger);
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
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        // Act
        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Assert — nothing fires before the initial delay elapses. The two advances split the
        // SAME armed timer, so only the first waits for it to be armed.
        await time.AdvanceOnNextTimerAsync(InitialDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Count.Should().Be(0);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await calls.WaitForAsync(1);
        calls.Count.Should().Be(1);
    }

    [Fact]
    public async Task RestartSuccess_SetsConnectionUp_RaisesConnectionRestored_AndLogsInformation()
    {
        // Arrange
        var time = CreateClock();
        var logger = CreateLogger();
        var restored = new AwaitableCounter();
        var sut = CreateSut(time, logger, restart: _ => Task.CompletedTask);
        sut.ConnectionRestored += restored.Record;

        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Act — ConnectionRestored is raised after the indicator is cleared, so waiting on it
        // makes both assertions below deterministic rather than only the first.
        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await restored.WaitForAsync(1);

        // Assert
        sut.IsConnectionDown.Should().BeFalse();
        restored.Count.Should().Be(1);
        VerifyLog(logger, LogLevel.Information, Times.AtLeastOnce());
    }

    [Fact]
    public async Task RestartAuthFailure_LogsWarning_AndSchedulesAnotherAttempt()
    {
        // Arrange — the ticket's test case: an auth failure during restart must be
        // logged and another attempt scheduled, not swallowed. The outage itself is
        // already reported at Error by OnClosed; per-attempt failures during a known
        // outage are expected-and-handled → Warning (logging skill).
        var time = CreateClock();
        var logger = CreateLogger();
        var calls = new AwaitableCounter();
        var restored = new AwaitableCounter();
        var sut = CreateSut(time, logger, restart: _ =>
        {
            calls.Record();
            if (calls.Count == 1)
            {
                throw new HttpRequestException(
                    "Response status code does not indicate success: 401 (Unauthorized).");
            }

            return Task.CompletedTask;
        });
        sut.ConnectionRestored += restored.Record;

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act — first attempt fails. The loop arms its next delay only after catching and logging
        // the failure, so waiting for that timer is what makes the log assertions deterministic.
        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await time.WaitForNextTimerAsync();

        // Assert — the outage is the only Error; the failed attempt logs a Warning
        // with the auth exception attached
        calls.Count.Should().Be(1);
        sut.IsConnectionDown.Should().BeTrue();
        VerifyLog(logger, LogLevel.Error, Times.Once(),
            messageContains: "closed permanently", withException: true);
        VerifyLog(logger, LogLevel.Warning, Times.Once(),
            messageContains: "restart attempt", withException: true);

        // Act — second attempt runs after the doubled delay and succeeds. The wait above already
        // consumed the armed timer, so both halves of this split advance are plain.
        time.Advance(MaxDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Count.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await restored.WaitForAsync(1);
        calls.Count.Should().Be(2);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task RestartDelays_AreCappedAtMaxRetryDelay()
    {
        // Arrange — delegate always fails; delays should follow 5s, 10s, 10s (capped), 10s...
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            throw new HttpRequestException("still unreachable");
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act / Assert — attempt 1 after the initial delay
        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await calls.WaitForAsync(1);

        // Attempt 2 after the doubled delay
        await time.AdvanceOnNextTimerAsync(MaxDelay);
        await calls.WaitForAsync(2);

        // Attempt 3 would be 20s uncapped — must fire at the 10s cap
        await time.AdvanceOnNextTimerAsync(MaxDelay);
        await calls.WaitForAsync(3);

        // Attempt 4 stays at the cap: nothing just before it, fires at it. Both advances act on
        // the SAME armed timer, so only the first waits for it.
        await time.AdvanceOnNextTimerAsync(MaxDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Count.Should().Be(3);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await calls.WaitForAsync(4);
        calls.Count.Should().Be(4);
    }

    [Fact]
    public async Task RestartLoop_WhenTheNextAdvanceIsIssuedMidAttempt_TheAdvanceIsNotLost()
    {
        // FHQ-158 regression. The coordinator arms its next delay at the TOP of the next loop
        // iteration — only after the failed attempt has been caught and logged. An advance issued
        // while an attempt is still in flight therefore lands on a clock with NO timer armed;
        // FakeTimeProvider drops it silently, the loop then arms its delay against the
        // already-advanced clock, and the following attempt never fires. Under CI load that
        // interleaving happened by chance and turned master #59 red (issue 10). Parking the
        // restart callback reproduces it deterministically: with a plain Advance here the advance
        // is swallowed and attempt 2 never runs.
        var time = CreateClock();
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: async _ =>
        {
            calls.Record();
            await parked.Task;
            throw new HttpRequestException("still unreachable");
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await calls.WaitForAsync(1); // attempt 1 is now parked mid-flight: no timer is armed

        // Act — issue the next advance mid-attempt, then let the attempt fail.
        var advance = time.AdvanceOnNextTimerAsync(MaxDelay);
        advance.IsCompleted.Should().BeFalse("the advance must be held until the loop re-arms");
        parked.SetResult();
        await advance;

        // Assert — the held advance fired attempt 2 instead of being swallowed.
        await calls.WaitForAsync(2);
        calls.Count.Should().Be(2);
    }

    [Fact]
    public void OnReconnecting_LogsWarning_AndSetsConnectionDown()
    {
        // Arrange
        var logger = CreateLogger();
        var sut = CreateSut(CreateClock(), logger);
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
        var sut = CreateSut(CreateClock());
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
        var sut = CreateSut(CreateClock(), logger);
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
        var time = CreateClock();
        var logger = CreateLogger();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, logger, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        // Act
        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Assert
        sut.IsConnectionDown.Should().BeTrue();
        VerifyLog(logger, LogLevel.Error, Times.Once(),
            messageContains: "closed permanently", withException: true);

        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await calls.WaitForAsync(1);
        calls.Count.Should().Be(1);
    }

    [Fact]
    public async Task OnReconnected_WhileRestartPending_CancelsScheduledRestart()
    {
        // Arrange — Closed starts the restart loop, then automatic reconnect wins the race
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act — the pending timer was cancelled and no further one can be armed, so this advance
        // is plain: there is nothing left to wait for.
        sut.OnReconnected();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Count.Should().Be(0);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task OnStarted_WhileRestartPending_CancelsScheduledRestart()
    {
        // Arrange — a failed first start schedules restarts; a later manual
        // StartAsync (e.g. revisiting the dashboard) succeeds first.
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Act
        sut.OnStarted();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Count.Should().Be(0);
        sut.IsConnectionDown.Should().BeFalse();
    }

    [Fact]
    public async Task RestartSuccess_ResetsBackoff_ForSubsequentOutage()
    {
        // Arrange
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var restored = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });
        sut.ConnectionRestored += restored.Record;

        sut.OnClosed(new InvalidOperationException("first outage"));
        await time.AdvanceOnNextTimerAsync(InitialDelay);
        // ConnectionRestored is the loop's last action before it returns, but the "a loop is
        // running" guard is cleared afterwards, in the loop's finally. Nothing observable is
        // written after that, so this wait rests on the scheduler resuming the loop's own
        // continuation ahead of ours — NOT on RunContinuationsAsynchronously, which only keeps the
        // waiter off the signaller's thread and orders nothing.
        // If that window ever does open, the second outage below is swallowed by the still-set
        // guard, no timer is armed, and AdvanceOnNextTimerAsync says so at its 5s deadline: a loud
        // failure naming the cause, never a silent wrong pass.
        await restored.WaitForAsync(1);
        calls.Count.Should().Be(1);

        // Act — a second outage must start again from the initial delay
        sut.OnClosed(new InvalidOperationException("second outage"));

        await time.AdvanceOnNextTimerAsync(InitialDelay - TimeSpan.FromMilliseconds(100));
        await YieldAsync();
        calls.Count.Should().Be(1);

        time.Advance(TimeSpan.FromMilliseconds(100));
        await calls.WaitForAsync(2);
        calls.Count.Should().Be(2);
    }

    [Fact]
    public async Task OnStartFailed_WhileRestartLoopRunning_DoesNotStartSecondLoop()
    {
        // Arrange
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        // Act — two failure reports, but only one loop may run. A second loop would arm a second
        // timer, which the advance below would fire alongside the first.
        sut.OnStartFailed(new InvalidOperationException("first failure"));
        sut.OnStartFailed(new InvalidOperationException("second failure"));

        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await calls.WaitForAsync(1);
        await YieldAsync();

        // Assert
        calls.Count.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_CancelsPendingRestart()
    {
        // Arrange
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("automatic reconnect exhausted"));

        // Act
        sut.Dispose();
        time.Advance(MaxDelay + MaxDelay);
        await YieldAsync();

        // Assert
        calls.Count.Should().Be(0);
    }

    [Fact]
    public async Task OnClosed_AfterCancelledRestartLoop_SchedulesRestartAgain()
    {
        // Arrange — a cancelled loop must never leave the coordinator believing a
        // loop is still running, or a later outage would go un-retried forever.
        var time = CreateClock();
        var calls = new AwaitableCounter();
        var sut = CreateSut(time, restart: _ =>
        {
            calls.Record();
            return Task.CompletedTask;
        });

        sut.OnClosed(new InvalidOperationException("first outage"));
        sut.OnStarted(); // cancels the pending restart loop

        // Act — a fresh outage after the cancellation
        sut.OnClosed(new InvalidOperationException("second outage"));
        await time.AdvanceOnNextTimerAsync(InitialDelay);
        await calls.WaitForAsync(1);

        // Assert
        calls.Count.Should().Be(1);
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
            CreateLogger().Object, CreateClock(), options);

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
            CreateLogger().Object, CreateClock(), options);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OnStartFailed_BeforeInitialize_Throws()
    {
        // Arrange — constructed directly so Initialize is never called
        var sut = new SignalRConnectionCoordinator(
            CreateLogger().Object, CreateClock(), CreateOptions());

        // Act
        var act = () => sut.OnStartFailed(new InvalidOperationException("connection refused"));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Initialize_CalledTwice_Throws()
    {
        // Arrange
        var sut = CreateSut(CreateClock());

        // Act
        var act = () => sut.Initialize(_ => Task.CompletedTask);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ---------- helpers ----------

    private static TimerArmedTimeProvider CreateClock() => new(new FakeTimeProvider());

    private static SignalRConnectionCoordinator CreateSut(
        TimerArmedTimeProvider time,
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
    /// <para>
    /// Only ever backs a NEGATIVE assertion ("nothing has fired yet"), where a budget exhausted
    /// under load can produce a false pass in a buggy-code scenario but never a false failure —
    /// and no finite wait can prove a negative anyway. Every positive wait observes the event
    /// itself (<see cref="AwaitableCounter"/>) and every advance observes timer registration
    /// (<see cref="TimerArmedTimeProvider"/>).
    /// </para>
    /// </summary>
    private static async Task YieldAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }
    }
}
