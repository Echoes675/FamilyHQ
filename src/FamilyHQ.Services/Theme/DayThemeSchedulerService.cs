using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaTime;

namespace FamilyHQ.Services.Theme;

public class DayThemeSchedulerService(
    IServiceProvider serviceProvider,
    IThemeBroadcaster themeBroadcaster,
    ILogger<DayThemeSchedulerService> logger,
    IOptions<DayThemeOptions> options,
    TimeProvider timeProvider) : BackgroundService, IDayThemeScheduler
{
    private readonly DayThemeOptions _options = options.Value;
    private CancellationTokenSource _delayCts = new();

    public Task TriggerRecalculationAsync()
    {
        var old = Interlocked.Exchange(ref _delayCts, new CancellationTokenSource());
        // FHQ-108: deliberately cancel WITHOUT disposing. A loop iteration may be holding this
        // instance, and both CancellationTokenSource.Token and Cancel() throw ObjectDisposedException
        // once disposed — the loop's ObjectDisposedException would be swallowed by its catch-all and
        // the recalculation lost. The abandoned source owns nothing to release: it has no timer
        // (CancelAfter is never used) and no wait handle (WaitHandle is never read), and cancelling it
        // has already run and released its registrations, so it is a plain managed object the GC
        // reclaims. Correctness beats tidiness.
        old.Cancel();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // FHQ-65: correlate the one-time startup broadcast.
        using (logger.BeginCorrelationScope())
        {
            // Startup wrapped in try/catch so a transient failure does not crash the hosted service.
            // Note this block only ever passes stoppingToken — the recalculation token is never linked
            // here, so a TriggerRecalculationAsync arriving during startup cannot cancel it; it is
            // served by the first loop iteration's fresh read instead.
            try
            {
                using var scope = serviceProvider.CreateScope();
                var kioskCount = await EnsureAllKiosksAsync(scope.ServiceProvider, stoppingToken);
                await themeBroadcaster.BroadcastThemeChangedAsync(stoppingToken);
                logger.LogInformation("Startup theme broadcast for {KioskCount} kiosk(s)", kioskCount);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Not shutdown, so this came from inside the theme service itself. Benign: the loop's
                // first iteration reads the boundaries again.
                logger.LogDebug("Startup theme broadcast cancelled without host shutdown; the loop will re-read the boundaries");
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
                    // Recalculation was triggered — loop restarts to re-read boundaries. FHQ-108: this
                    // is the only evidence a trigger was ever honoured; without it a recurrence of the
                    // silently-lost recalculation would be exactly as invisible as the original bug.
                    // Information, not Debug, because prod runs at Information and a trigger only ever
                    // comes from a human saving or clearing a location — it cannot flood.
                    logger.LogInformation("Theme recalculation triggered; re-reading day-theme boundaries");
                }
                catch (OperationCanceledException)
                {
                    // Host is shutting down — exit the loop cleanly
                    logger.LogDebug("DayThemeScheduler loop stopping; host shutdown requested");
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

    private async Task DelayQuietlyAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        // Swallow cancellation during the backoff so shutdown is graceful; the loop condition exits next.
        try
        {
            await Task.Delay(delay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — the loop condition exits on the next check.
            logger.LogDebug("DayThemeScheduler error backoff cancelled; host shutdown requested");
        }
    }

    private async Task RunLoopIterationAsync(CancellationToken stoppingToken)
    {
        // FHQ-108: snapshot the recalculation token ONCE, before any awaited work, and use that value
        // for the rest of the iteration. Reading the field later (after the boundary read) would let a
        // TriggerRecalculationAsync that landed mid-iteration install a fresh, uncancelled source that
        // this iteration then waits on — sleeping on boundaries read before the location changed, so
        // the recalculation is silently lost. A CancellationToken value stays usable no matter what
        // happens to the field afterwards.
        var recalculationToken = Volatile.Read(ref _delayCts).Token;

        TimeSpan nextBoundary;
        using (var scope = serviceProvider.CreateScope())
        {
            await EnsureAllKiosksAsync(scope.ServiceProvider, stoppingToken);
            nextBoundary = await ComputeNextBoundaryDelayAsync(scope.ServiceProvider, stoppingToken);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, recalculationToken);
        // Delay through the injected TimeProvider, like every boundary computation above, so the whole
        // iteration honours one clock. With TimeProvider.System this is the ambient Task.Delay; under
        // FakeTimeProvider the wait becomes drivable, which is what makes the delay-elapses path testable.
        await Task.Delay(nextBoundary, timeProvider, linkedCts.Token);

        // The delay may have crossed midnight; ensure again before the post-delay read so the new
        // calendar day's rows are present. This closes the FHQ-55 race in which the rollover check
        // and the read straddled the date boundary.
        using (var scope = serviceProvider.CreateScope())
        {
            await EnsureAllKiosksAsync(scope.ServiceProvider, stoppingToken);
        }

        // Broadcast AFTER the delay so kiosks re-read the period that just became active. The signal
        // carries no period: it is per-kiosk now, and each kiosk fetches its own.
        await themeBroadcaster.BroadcastThemeChangedAsync(stoppingToken);
        logger.LogInformation("Theme boundary reached; kiosks signalled to re-read");
    }

    /// <summary>
    /// Ensures today's row exists for every kiosk that has a saved location. A single kiosk's failure
    /// (unusable coordinates, a polar latitude with no sun phase) must not deny every other kiosk its
    /// theme, so each is guarded independently — the loop's catch-all would have abandoned the whole
    /// pass.
    /// </summary>
    private async Task<int> EnsureAllKiosksAsync(IServiceProvider scoped, CancellationToken ct)
    {
        var locationRepo = scoped.GetRequiredService<ILocationSettingRepository>();
        var dayThemeService = scoped.GetRequiredService<IDayThemeService>();

        var userIds = await locationRepo.GetUserIdsWithLocationAsync(ct);
        foreach (var userId in userIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await dayThemeService.EnsureTodayAsync(userId, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // No UserId in the message: it is a stable pseudonymous id, but the failure text can
                // carry coordinates from the sun calculator, so keep both out (FHQ-166).
                logger.LogError(ex, "Day-theme calculation failed for one kiosk; continuing with the others");
            }
        }

        return userIds.Count;
    }

    /// <summary>
    /// The delay until the EARLIEST upcoming boundary across every kiosk. Kiosks in different zones
    /// cross their boundaries at different instants, so the loop must wake for whichever comes first
    /// and let each kiosk work out whether anything changed for it.
    /// </summary>
    private async Task<TimeSpan> ComputeNextBoundaryDelayAsync(IServiceProvider scoped, CancellationToken ct)
    {
        var locationRepo = scoped.GetRequiredService<ILocationSettingRepository>();
        var dayThemeService = scoped.GetRequiredService<IDayThemeService>();

        var userIds = await locationRepo.GetUserIdsWithLocationAsync(ct);

        TimeSpan? earliest = null;
        foreach (var userId in userIds)
        {
            var dto = await dayThemeService.GetTodayAsync(userId, ct);
            if (dto is null) continue;

            var delay = GetNextBoundaryDelay(dto);
            if (earliest is null || delay < earliest) earliest = delay;
        }

        // No kiosk has a usable theme (none configured, or every calculation failed). Re-check on the
        // error backoff rather than spinning, and rather than sleeping until a midnight we cannot
        // locate without a zone.
        return earliest ?? _options.LoopErrorBackoff;
    }

    protected TimeSpan GetNextBoundaryDelay(Core.DTOs.DayThemeDto dto)
    {
        var zone = !string.IsNullOrWhiteSpace(dto.IanaTimeZone)
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(dto.IanaTimeZone)
            : null;

        var localNow = ComputeLocalNow(zone);
        var boundaries = new[] { dto.MorningStart, dto.DaytimeStart, dto.EveningStart, dto.NightStart };
        var next = boundaries.Cast<TimeOnly?>().Where(b => b > localNow).OrderBy(b => b).FirstOrDefault();

        if (next is null)
            return ComputeDelayToLocalMidnight(zone);

        return ComputeDelayToLocalTime(next.Value, zone);
    }

    private TimeOnly ComputeLocalNow(DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var instant = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
            var local = instant.InZone(zone).LocalDateTime;
            return new TimeOnly(local.Hour, local.Minute, local.Second);
        }
        return TimeOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
    }

    private TimeSpan ComputeDelayToLocalMidnight(DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var nowInstant = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
            var localDate = nowInstant.InZone(zone).Date;
            var midnight = zone.AtStartOfDay(localDate.PlusDays(1)).ToInstant();
            return (midnight - nowInstant).ToTimeSpan();
        }
        var now = timeProvider.GetLocalNow();
        return now.Date.AddDays(1) - now.DateTime;
    }

    private TimeSpan ComputeDelayToLocalTime(TimeOnly localTime, DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var nowInstant = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
            var localDate = nowInstant.InZone(zone).Date;
            var targetLocal = localDate.At(new LocalTime(localTime.Hour, localTime.Minute, localTime.Second));
            // AtLeniently: spring-forward gaps delay wakeup by up to the gap duration; fall-back ambiguity
            // picks the pre-transition instant. Both are acceptable for a UI theme scheduler.
            var targetInstant = zone.AtLeniently(targetLocal).ToInstant();
            var delay = (targetInstant - nowInstant).ToTimeSpan();
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
        var now = timeProvider.GetLocalNow();
        var nextDateTime = now.Date.Add(localTime.ToTimeSpan());
        var delayTs = nextDateTime - now.DateTime;
        return delayTs < TimeSpan.Zero ? TimeSpan.Zero : delayTs;
    }
}
