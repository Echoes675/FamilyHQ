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
        using var scope = serviceProvider.CreateScope();
        var dayThemeService = scope.ServiceProvider.GetRequiredService<IDayThemeService>();
        await dayThemeService.EnsureTodayAsync(stoppingToken);

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
        var currentPeriod = dto.CurrentPeriod;

        await themeBroadcaster.BroadcastThemeAsync(currentPeriod, stoppingToken);
        logger.LogInformation("Theme broadcast: {Period}", currentPeriod);

        var nextBoundary = GetNextBoundaryDelay(dto);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _delayCts.Token);
        await Task.Delay(nextBoundary, linkedCts.Token);

        // After delay: check if it's a new day and ensure today's record exists
        var todayRecord = await dayThemeService.GetTodayAsync(stoppingToken);
        if (todayRecord.Date != DateOnly.FromDateTime(DateTime.Today))
        {
            await dayThemeService.EnsureTodayAsync(stoppingToken);
        }
    }

    private static TimeSpan GetNextBoundaryDelay(Core.DTOs.DayThemeDto dto)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var boundaries = new[] { dto.MorningStart, dto.DaytimeStart, dto.EveningStart, dto.NightStart };
        var next = boundaries.Where(b => b > now).OrderBy(b => b).FirstOrDefault();

        if (next == default)
        {
            // All boundaries passed — wait until midnight
            var midnight = DateTime.Today.AddDays(1);
            return midnight - DateTime.Now;
        }

        var nextDateTime = DateTime.Today.Add(next.ToTimeSpan());
        var delay = nextDateTime - DateTime.Now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
