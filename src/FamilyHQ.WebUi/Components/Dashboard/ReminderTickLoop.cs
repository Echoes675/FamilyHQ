using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Owns one <see cref="PeriodicUiRefreshLoop"/>'s lifetime so nothing calling this can leave an old
/// loop running while starting a new one over it.
/// </summary>
/// <remarks>
/// <para>
/// Blazor dispatches the next UI event while a component method is suspended on an <c>await</c>, so
/// "switch to the Reminders tab, switch away, switch back before the first fetch resolves" is an
/// ordinary sequence on a kiosk, not a rare one. A start/stop pair that looks correct in isolation —
/// start when entering the tab, stop when leaving it — can still orphan a loop under that
/// interleaving: a stop can run while the matching start has not yet recorded its handles (so it
/// no-ops), and the start can then resume and record a loop for a view the user already left,
/// un-cancellable short of a page reload.
/// </para>
/// <para>
/// <see cref="EnsureRunningAsync"/> and <see cref="StopAsync"/> are serialised against each other
/// with an internal gate, not merely written to look sequential. Awaiting the outgoing loop's
/// shutdown is itself a real suspension — the tick this type drives can be mid a server refetch when
/// a stop arrives — and without the gate a second call can land in exactly that window: it finds the
/// fields already cleared, so its own stop step no-ops, and it records a loop of its own; the first
/// call then resumes and overwrites those handles with a third loop, orphaning the second one — the
/// same bug one layer down, reachable only through a stop-in-progress rather than a bare double
/// start. The gate closes that: whichever of two overlapping calls gets in first runs its entire
/// stop-then-start (or stop) to completion before the other is even allowed to read the fields, so
/// there is never a point where two loops exist or where a stop can race a start that has not yet
/// published its handles for the stop to find — not "unlikely to", but structurally cannot.
/// </para>
/// </remarks>
public sealed class ReminderTickLoop : IDisposable
{
    // Guards _cts/_loop/_logger as one unit across EnsureRunningAsync and StopAsync. A SemaphoreSlim
    // rather than `lock`: both methods hold it across an `await`, which `lock` cannot do.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ILogger? _logger;

    /// <summary>Whether a loop is currently running. For tests; production code never branches on it.</summary>
    public bool IsRunning => _cts is not null;

    /// <summary>
    /// Stops whatever loop is already running — fully awaited, not merely cancelled — then starts a
    /// fresh one. Idempotent: calling this while already running still ends with exactly one loop,
    /// never two and never zero, even against a second overlapping call to this or to
    /// <see cref="StopAsync"/>.
    /// </summary>
    public async Task EnsureRunningAsync(TimeProvider clock, TimeSpan period, Func<Task> refreshAsync, ILogger logger)
    {
        await _gate.WaitAsync();
        try
        {
            await StopLockedAsync();

            _logger = logger;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = PeriodicUiRefreshLoop.RunAsync(clock, period, refreshAsync, logger, cts.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Cancels the running loop and awaits its shutdown before returning. A no-op — not an
    /// exception — when nothing is running, so a stop that races a start which has not started yet,
    /// or a stop called twice, is always safe.
    /// </summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    // The actual stop, assuming the gate is already held — never called except from inside the two
    // gated methods above. Pulled out rather than having EnsureRunningAsync call the public StopAsync
    // directly, which would try to re-acquire a non-reentrant semaphore this same call already holds.
    private async Task StopLockedAsync()
    {
        if (_cts is null) return;

        // Cleared before awaiting, not after: IsRunning must read false for the whole time this
        // is shutting the loop down, not just once it has finished.
        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        cts.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (Exception ex)
            {
                // PeriodicUiRefreshLoop.RunAsync already catches and logs its own tick failures and
                // its own cancellation; reaching here would mean something escaped that loop's own
                // handling entirely, which is still not a reason to fail whatever is stopping it.
                _logger?.LogDebug(ex, "Reminders tick loop faulted while stopping");
            }
        }

        cts.Dispose();
    }

    /// <summary>
    /// Releases the gate itself. Callers that need the loop stopped first must call
    /// <see cref="StopAsync"/> and await it before this — disposing is not itself a stop, and doing
    /// so while <see cref="EnsureRunningAsync"/> or <see cref="StopAsync"/> is still in flight would
    /// fault that call's own <c>WaitAsync</c> rather than cleanly cancel it.
    /// </summary>
    public void Dispose() => _gate.Dispose();
}
