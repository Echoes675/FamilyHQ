using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Theme;

public class DayThemeSchedulerService(
    IServiceProvider serviceProvider,
    IThemeBroadcaster themeBroadcaster,
    ILogger<DayThemeSchedulerService> logger,
    IOptions<DayThemeOptions> options) : BackgroundService, IDayThemeScheduler
{
    private readonly DayThemeOptions _options = options.Value;
    private CancellationTokenSource _delayCts = new();

    public Task TriggerRecalculationAsync()
    {
        var old = Interlocked.Exchange(ref _delayCts, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // FHQ-65: correlate the one-time startup broadcast.
        using (logger.BeginCorrelationScope())
        {
            // Startup wrapped in try/catch so a TriggerRecalculationAsync race or
            // transient failure does not crash the hosted service.
            try
            {
                using var scope = serviceProvider.CreateScope();
                var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();
                await dayThemeService.EnsureTodayAsync(stoppingToken);
                var dto = await dayThemeService.GetTodayAsync(stoppingToken);
                await themeBroadcaster.BroadcastThemeAsync(dto.CurrentPeriod, stoppingToken);
                logger.LogInformation("Startup theme broadcast: {Period}", dto.CurrentPeriod);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Recalculation triggered during startup — loop will re-read boundaries
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Startup theme initialization failed");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // FHQ-65: fresh CorrelationId per scheduling iteration.
            using (logger.BeginCorrelationScope())
            {
                try
                {
                    await RunLoopIterationAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Recalculation was triggered — loop restarts to re-read boundaries
                }
                catch (OperationCanceledException)
                {
                    // Host is shutting down — exit the loop cleanly
                    break;
                }
                catch (Exception ex)
                {
                    // FHQ-55: never let a loop-iteration failure (e.g. a missing DayTheme record at a day
                    // boundary, or a transient DB/location/sun-calc error) propagate to the host, which runs
                    // with BackgroundServiceExceptionBehavior.StopHost and would otherwise stop the whole app.
                    // Log the failure and continue after a backoff so we don't hot-loop on a persistent fault.
                    logger.LogError(ex, "DayThemeScheduler loop iteration failed; continuing after {Backoff}", _options.LoopErrorBackoff);
                    await DelayQuietlyAsync(_options.LoopErrorBackoff, stoppingToken);
                }
            }
        }
    }

    private static async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        // Swallow cancellation during the backoff so shutdown is graceful; the loop condition exits next.
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — nothing to log; the loop condition exits on the next check.
        }
    }

    private async Task RunLoopIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();

        // Always ensure today's record exists before reading it. EnsureTodayAsync is idempotent
        // (a single indexed read when the record is already present), so this is cheap, and it removes
        // the day-boundary race where the date rolled over after the previous iteration's delay.
        await dayThemeService.EnsureTodayAsync(stoppingToken);
        var dto = await dayThemeService.GetTodayAsync(stoppingToken);

        var nextBoundary = GetNextBoundaryDelay(dto);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _delayCts.Token);
        await Task.Delay(nextBoundary, linkedCts.Token);

        // The delay may have crossed midnight; ensure again before the post-delay read so the new
        // calendar day's record is present. This closes the FHQ-55 race in which the rollover check
        // and the read straddled the date boundary, leaving GetTodayAsync to throw and crash the host.
        await dayThemeService.EnsureTodayAsync(stoppingToken);

        // Broadcast AFTER the delay so we emit the period that just became active
        var updatedDto = await dayThemeService.GetTodayAsync(stoppingToken);
        await themeBroadcaster.BroadcastThemeAsync(updatedDto.CurrentPeriod, stoppingToken);
        logger.LogInformation("Theme broadcast: {Period}", updatedDto.CurrentPeriod);
    }

    private static TimeSpan GetNextBoundaryDelay(Core.DTOs.DayThemeDto dto)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var boundaries = new[] { dto.MorningStart, dto.DaytimeStart, dto.EveningStart, dto.NightStart };
        // Use TimeOnly? so default(TimeOnly) midnight is never mistaken for "no boundary"
        var next = boundaries.Cast<TimeOnly?>().Where(b => b > now).OrderBy(b => b).FirstOrDefault();

        if (next is null)
        {
            // All boundaries passed — wait until midnight
            var midnight = DateTime.Today.AddDays(1);
            return midnight - DateTime.Now;
        }

        var nextDateTime = DateTime.Today.Add(next.Value.ToTimeSpan());
        var delay = nextDateTime - DateTime.Now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
