using Microsoft.Extensions.Logging;

namespace FamilyHQ.WebUi.Components;

/// <summary>
/// Drives a component's periodic re-render (FHQ-127 now-line, FHQ-131 header clock): ticks
/// once per period on the supplied clock and invokes the refresh callback. A failed tick is
/// logged and the loop keeps running — a dead loop would silently freeze the UI element it
/// drives — while cancellation exits it. Extracted from the owning components so the
/// resilience behaviour is unit-testable with FakeTimeProvider (no bUnit, no real timers).
/// </summary>
public static class PeriodicUiRefreshLoop
{
    /// <param name="clock">Time source the periodic timer runs on (fake in tests).</param>
    /// <param name="period">Interval between refresh ticks.</param>
    /// <param name="refreshAsync">Re-render callback; components pass InvokeAsync(StateHasChanged).</param>
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
                    logger.LogWarning(ex, "Periodic UI refresh tick failed; retrying on the next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Periodic UI refresh loop stopped by cancellation");
        }
    }
}
