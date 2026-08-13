namespace FamilyHQ.WebUi.Services;

/// <summary>
/// Default <see cref="ISignalRConnectionCoordinator"/> implementation (FHQ-125).
/// Logs every connection-lifecycle transition, tracks the stale-data indicator
/// state, and — when the connection is permanently down (initial start failed or
/// automatic reconnect exhausted) — keeps trying to restore it with bounded
/// exponential backoff. Runs on the single-threaded Blazor WASM dispatcher, so
/// no locking is required around the state fields.
/// </summary>
public sealed class SignalRConnectionCoordinator(
    ILogger<SignalRConnectionCoordinator> logger,
    TimeProvider timeProvider,
    SignalRReconnectOptions options) : ISignalRConnectionCoordinator, IDisposable
{
    private readonly ILogger<SignalRConnectionCoordinator> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly SignalRReconnectOptions _options = ValidateOptions(options);

    private Func<CancellationToken, Task>? _restartAsync;
    private CancellationTokenSource? _restartLoopCts;
    private bool _restartLoopRunning;
    private bool _connectionDown;
    private bool _disposed;

    public bool IsConnectionDown => _connectionDown;

    public event Action? ConnectionStateChanged;
    public event Action? ConnectionRestored;

    public void Initialize(Func<CancellationToken, Task> restartAsync)
    {
        ArgumentNullException.ThrowIfNull(restartAsync);
        if (_restartAsync is not null)
        {
            throw new InvalidOperationException(
                $"{nameof(Initialize)} has already been called; the restart callback cannot be replaced.");
        }

        _restartAsync = restartAsync;
    }

    public void OnStarted()
    {
        _logger.LogInformation("SignalR connected to the calendar hub");
        CancelRestartLoop();
        SetConnectionDown(false);
    }

    public void OnStartFailed(Exception exception)
    {
        EnsureInitialized();
        _logger.LogError(exception,
            "Initial SignalR connection failed; live updates are unavailable until a background restart succeeds");
        // Loop first: a throwing ConnectionStateChanged subscriber must not
        // prevent the restart schedule from starting.
        BeginRestartLoop();
        SetConnectionDown(true);
    }

    public void OnReconnecting(Exception? exception)
    {
        _logger.LogWarning(exception, "SignalR connection lost; automatic reconnect in progress");
        SetConnectionDown(true);
    }

    public void OnReconnected()
    {
        _logger.LogInformation("SignalR connection restored by automatic reconnect");
        CancelRestartLoop();
        SetConnectionDown(false);
        ConnectionRestored?.Invoke();
    }

    public void OnClosed(Exception? exception)
    {
        EnsureInitialized();
        _logger.LogError(exception,
            "SignalR connection closed permanently (automatic reconnect exhausted); scheduling background restart attempts");
        // Loop first: a throwing ConnectionStateChanged subscriber must not
        // prevent the restart schedule from starting.
        BeginRestartLoop();
        SetConnectionDown(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelRestartLoop();
    }

    private void EnsureInitialized()
    {
        if (_restartAsync is null)
        {
            throw new InvalidOperationException(
                $"{nameof(Initialize)} must be called with a restart callback before connection events are reported.");
        }
    }

    private void BeginRestartLoop()
    {
        if (_restartLoopRunning || _disposed)
        {
            return;
        }

        _restartLoopCts?.Dispose();
        _restartLoopCts = new CancellationTokenSource();
        _restartLoopRunning = true;

        // Fire-and-forget by design: the loop observes and logs every exception
        // internally, so the discarded task can never fault unobserved.
        _ = RunRestartLoopAsync(_restartLoopCts.Token);
    }

    private void CancelRestartLoop()
    {
        if (_restartLoopCts is { } cts)
        {
            _restartLoopCts = null;
            cts.Cancel();
            cts.Dispose();

            // The cancelled loop's own finally runs as a queued continuation, so it
            // may not have observed the cancellation yet. Clear the flag here (safe:
            // a cancelled loop never invokes the restart callback again) so a new
            // outage reported in that window can still begin a fresh loop.
            _restartLoopRunning = false;
        }
    }

    private async Task RunRestartLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                attempt++;
                var delay = ComputeDelay(attempt);
                _logger.LogInformation(
                    "SignalR restart attempt {Attempt} scheduled in {Delay}", attempt, delay);

                await Task.Delay(delay, _timeProvider, ct);

                try
                {
                    await _restartAsync!(ct);
                    _logger.LogInformation(
                        "SignalR connection restored after {Attempt} background restart attempt(s)", attempt);
                    SetConnectionDown(false);
                    ConnectionRestored?.Invoke();
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The outage itself was already reported at Error by
                    // OnStartFailed/OnClosed; a failed attempt during a known
                    // outage is expected-and-handled → Warning (logging skill).
                    _logger.LogWarning(ex,
                        "SignalR restart attempt {Attempt} failed; will retry with backoff", attempt);
                }
            }

            _logger.LogDebug("SignalR restart loop stopped: connection restored elsewhere or shutting down");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("SignalR restart loop cancelled: connection restored elsewhere or shutting down");
        }
        catch (Exception ex)
        {
            // Safety net for the fire-and-forget task — must never fault unobserved.
            _logger.LogError(ex, "SignalR restart loop terminated unexpectedly");
        }
        finally
        {
            // A cancelled loop's flag was already cleared by CancelRestartLoop, and
            // a successor loop may own it by now — only non-cancelled exits
            // (success or the safety net) may clear it here.
            if (!ct.IsCancellationRequested)
            {
                _restartLoopRunning = false;
            }
        }
    }

    private static SignalRReconnectOptions ValidateOptions(SignalRReconnectOptions options)
    {
        // Fail fast at startup: these are config-bound, and bad values would either
        // spin a hot restart loop (zero/negative) or break the backoff invariant.
        if (options.InitialRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.InitialRetryDelay,
                $"{nameof(SignalRReconnectOptions.InitialRetryDelay)} must be positive.");
        }

        if (options.MaxRetryDelay < options.InitialRetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetryDelay,
                $"{nameof(SignalRReconnectOptions.MaxRetryDelay)} must be greater than or equal to " +
                $"{nameof(SignalRReconnectOptions.InitialRetryDelay)}.");
        }

        return options;
    }

    private TimeSpan ComputeDelay(int attempt)
    {
        // Bounded exponential backoff: initial, x2 per failed attempt, capped at max.
        // The exponent itself is clamped so the multiplication can never overflow.
        var exponent = Math.Min(attempt - 1, 20);
        var delay = _options.InitialRetryDelay * Math.Pow(2, exponent);
        return delay > _options.MaxRetryDelay ? _options.MaxRetryDelay : delay;
    }

    private void SetConnectionDown(bool down)
    {
        if (_connectionDown == down)
        {
            return;
        }

        _connectionDown = down;
        ConnectionStateChanged?.Invoke();
    }
}
