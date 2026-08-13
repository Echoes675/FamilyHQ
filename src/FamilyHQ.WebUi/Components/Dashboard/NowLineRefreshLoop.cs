using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebUi.Components.Dashboard;

/// <summary>
/// Drives the Day view "now" indicator refresh (FHQ-127): ticks once per period on the
/// supplied clock and invokes the refresh callback. A failed tick is logged and the loop
/// keeps running — a dead loop would silently reinstate the frozen-line bug — while
/// cancellation exits it. Extracted from DayView so the resilience behaviour is
/// unit-testable with FakeTimeProvider (no bUnit, no real timers).
/// </summary>
public static class NowLineRefreshLoop
{
    /// <param name="clock">Time source the periodic timer runs on (fake in tests).</param>
    /// <param name="period">Interval between refresh ticks.</param>
    /// <param name="refreshAsync">Re-render callback; DayView passes InvokeAsync(StateHasChanged).</param>
    /// <param name="logger">Sink for tick-failure warnings and the shutdown debug entry.</param>
    /// <param name="token">Stops the loop when the owning component is disposed.</param>
    public static async Task RunAsync(
        TimeProvider clock,
        TimeSpan period,
        Func<Task> refreshAsync,
        ILogger logger,
        CancellationToken token)
    {
        using var timer = new PeriodicTimer(period, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    await refreshAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Now-line refresh tick failed; retrying on the next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Now-line refresh loop stopped by cancellation");
        }
    }
}
