using FamilyHQ.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Theme;

public class DayThemeSchedulerService(
    IServiceProvider serviceProvider,
    IThemeBroadcaster themeBroadcaster,
    ILogger<DayThemeSchedulerService> logger) : BackgroundService, IDayThemeScheduler
{
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
        // Fix 3: startup wrapped in try/catch so a TriggerRecalculationAsync race or
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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLoopIterationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Recalculation was triggered — loop restarts to re-read boundaries
            }
        }
    }

    private async Task RunLoopIterationAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();

        var dto = await dayThemeService.GetTodayAsync(stoppingToken);
        // Fix 2: capture date before the delay so midnight-crossing can be detected
        var dateBeforeDelay = dto.Date;

        var nextBoundary = GetNextBoundaryDelay(dto);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _delayCts.Token);
        await Task.Delay(nextBoundary, linkedCts.Token);

        // After delay: if the calendar date rolled over, ensure today's record exists
        if (dateBeforeDelay != DateOnly.FromDateTime(DateTime.Today))
        {
            await dayThemeService.EnsureTodayAsync(stoppingToken);
        }

        // Fix 1: broadcast AFTER the delay so we emit the period that just became active
        var updatedDto = await dayThemeService.GetTodayAsync(stoppingToken);
        await themeBroadcaster.BroadcastThemeAsync(updatedDto.CurrentPeriod, stoppingToken);
        logger.LogInformation("Theme broadcast: {Period}", updatedDto.CurrentPeriod);
    }

    private static TimeSpan GetNextBoundaryDelay(Core.DTOs.DayThemeDto dto)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var boundaries = new[] { dto.MorningStart, dto.DaytimeStart, dto.EveningStart, dto.NightStart };
        // Fix 4: use TimeOnly? so default(TimeOnly) midnight is never mistaken for "no boundary"
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
