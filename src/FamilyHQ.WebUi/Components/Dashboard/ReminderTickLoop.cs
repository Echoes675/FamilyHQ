using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Owns one <see cref="PeriodicUiRefreshLoop"/>'s lifetime so nothing calling this can leave an old
/// loop running while starting a new one over it.
/// </summary>
/// <remarks>
/// Blazor dispatches the next UI event while a component method is suspended on an <c>await</c>, so
/// "switch to the Reminders tab, switch away, switch back before the first fetch resolves" is an
/// ordinary sequence on a kiosk, not a rare one. A start/stop pair that looks correct in isolation —
/// start when entering the tab, stop when leaving it — can still orphan a loop under that
/// interleaving: a stop can run while the matching start has not yet recorded its handles (so it
/// no-ops), and the start can then resume and record a loop for a view the user already left,
/// un-cancellable short of a page reload. This type closes that off structurally rather than by
/// getting every caller's ordering right: <see cref="EnsureRunningAsync"/> always fully stops
/// whatever is already running — cancelled AND awaited, not just cancelled and abandoned — before
/// recording the new handles. However many overlapping start/stop calls land in whatever order, there
/// is never a point where two loops exist or where a stop can race a start that has not yet published
/// its handles for the stop to find.
/// </remarks>
public sealed class ReminderTickLoop
{
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private ILogger? _logger;

    /// <summary>Whether a loop is currently running. For tests; production code never branches on it.</summary>
    public bool IsRunning => _cts is not null;

    /// <summary>
    /// Stops whatever loop is already running — fully awaited, not merely cancelled — then starts a
    /// fresh one. Idempotent: calling this while already running still ends with exactly one loop,
    /// never two and never zero.
    /// </summary>
    public async Task EnsureRunningAsync(TimeProvider clock, TimeSpan period, Func<Task> refreshAsync, ILogger logger)
    {
        await StopAsync();

        _logger = logger;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _loop = PeriodicUiRefreshLoop.RunAsync(clock, period, refreshAsync, logger, cts.Token);
    }

    /// <summary>
    /// Cancels the running loop and awaits its shutdown before returning. A no-op — not an
    /// exception — when nothing is running, so a stop that races a start which has not started yet,
    /// or a stop called twice, is always safe.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;

        // Cleared before awaiting, not after: IsRunning must read false for the whole time this
        // method is shutting the loop down, not just once it has finished.
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
}
