namespace FamilyHQ.Services.Weather;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Logging;
using FamilyHQ.Core.Models;
using FamilyHQ.Services.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// FHQ-109: polls weather per user on a schedule that escalates for whoever is failing. Before this,
/// every cycle slept <c>Max(MinPollIntervalMinutes, …)</c> regardless of outcome, so a rate-limited
/// Open-Meteo was re-hit every 60 seconds forever — hammering the API and flooding Seq.
/// </summary>
public class WeatherPollerService(
    IServiceProvider serviceProvider,
    IOptions<WeatherOptions> options,
    TimeProvider timeProvider,
    ILogger<WeatherPollerService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>Consecutive failures between Warning-level reminders once the interval stops escalating.</summary>
    private const int SustainedFailureLogInterval = 10;

    private readonly WeatherOptions _options = options.Value;

    // Per-user backoff state. The poll loop is a single hosted service running one cycle at a time,
    // so no synchronisation is needed. Keys are pruned to the current enabled-user set every cycle,
    // which bounds this by the number of users with weather enabled right now — never by the number
    // of users seen historically.
    //
    // Deliberately NOT shared with the on-demand path: a successful POST /api/weather/refresh does
    // not clear a user's backoff here, so the poller keeps its own escalated interval until its next
    // scheduled attempt succeeds. That is safe (the user already has fresh data, and the poller only
    // ever waits longer, never hammers), and it keeps this state single-writer.
    private readonly Dictionary<string, UserPollState> _pollStates = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, timeProvider, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // FHQ-65: fresh CorrelationId per poll cycle.
            using (logger.BeginCorrelationScope())
            {
                try
                {
                    await RunPollIterationAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Weather poll iteration failed. Retrying in {Delay}.", RetryDelay);
                    await Task.Delay(RetryDelay, timeProvider, stoppingToken);
                }
            }
        }
    }

    private async Task RunPollIterationAsync(CancellationToken stoppingToken)
    {
        var delay = await RunPollCycleAsync(stoppingToken);
        await Task.Delay(delay, timeProvider, stoppingToken);
    }

    /// <summary>
    /// Refreshes every enabled user that is due, updates their backoff state, and returns how long
    /// the loop should sleep before the next cycle (the earliest due time, floored at
    /// <see cref="WeatherOptions.MinPollIntervalMinutes"/> so the loop can never spin).
    /// </summary>
    protected async Task<TimeSpan> RunPollCycleAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var refreshService = scope.ServiceProvider.GetRequiredService<IWeatherRefreshService>();
        var weatherSettingRepo = scope.ServiceProvider.GetRequiredService<IWeatherSettingRepository>();

        var allSettings = await weatherSettingRepo.GetAllAsync(stoppingToken);
        var enabledSettings = allSettings.Where(s => s.Enabled).ToList();

        PruneStatesFor(enabledSettings);

        foreach (var setting in enabledSettings)
        {
            if (_pollStates.TryGetValue(setting.UserId, out var state) && state.NextAttemptUtc > timeProvider.GetUtcNow())
                continue; // still backed off — not due yet

            try
            {
                await refreshService.RefreshAsync(setting.UserId, stoppingToken);
                RecordSuccess(setting);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                RecordFailure(setting, ex);
            }
        }

        return ComputeSleep(enabledSettings);
    }

    private void RecordSuccess(WeatherSetting setting)
    {
        var interval = BaseInterval(setting);

        if (_pollStates.TryGetValue(setting.UserId, out var previous) && previous.ConsecutiveFailures > 0)
            logger.LogInformation(
                "Weather refresh recovered for user {UserId} after {Failures} consecutive failures; poll interval reset to {IntervalMinutes} min.",
                setting.UserId, previous.ConsecutiveFailures, interval.TotalMinutes);

        _pollStates[setting.UserId] = new UserPollState(0, interval, timeProvider.GetUtcNow() + interval);
    }

    private void RecordFailure(WeatherSetting setting, Exception ex)
    {
        _pollStates.TryGetValue(setting.UserId, out var previous);
        var failures = (previous?.ConsecutiveFailures ?? 0) + 1;
        var interval = EscalatedInterval(BaseInterval(setting), failures);

        // Log the escalation transition, not every cycle: once the interval plateaus at the cap a
        // permanently broken provider would otherwise emit one Error per poll forever. Flood control
        // must not become silence though — Debug is off in production, so a still-unrecovered outage
        // is re-surfaced at Warning every SustainedFailureLogInterval attempts.
        if (interval != previous?.Interval)
            logger.LogError(ex,
                "Weather refresh failed for user {UserId} ({Failures} consecutive); next attempt in {IntervalMinutes} min.",
                setting.UserId, failures, interval.TotalMinutes);
        else if (failures % SustainedFailureLogInterval == 0)
            logger.LogWarning(ex,
                "Weather refresh still failing for user {UserId} ({Failures} consecutive); poll interval held at {IntervalMinutes} min.",
                setting.UserId, failures, interval.TotalMinutes);
        else
            logger.LogDebug(ex,
                "Weather refresh still failing for user {UserId} ({Failures} consecutive); poll interval held at {IntervalMinutes} min.",
                setting.UserId, failures, interval.TotalMinutes);

        _pollStates[setting.UserId] = new UserPollState(failures, interval, timeProvider.GetUtcNow() + interval);
    }

    private TimeSpan ComputeSleep(List<WeatherSetting> enabledSettings)
    {
        var floor = TimeSpan.FromMinutes(_options.MinPollIntervalMinutes);
        if (enabledSettings.Count == 0)
            return floor;

        var now = timeProvider.GetUtcNow();
        var earliestDue = enabledSettings
            .Select(s => _pollStates.TryGetValue(s.UserId, out var state) ? state.NextAttemptUtc : now)
            .Min();

        var untilDue = earliestDue - now;
        if (untilDue < floor)
            return floor;

        // Discovery ceiling: a backed-off user must not stop the loop noticing someone who has just
        // enabled weather. Before FHQ-109 the loop woke at least every Min(user PollIntervalMinutes),
        // so cap the wait at the configured default interval to keep that guarantee. A cycle that
        // wakes with nobody due is a single repository read.
        var discoveryCeiling = TimeSpan.FromMinutes(_options.PollIntervalMinutes);
        return untilDue > discoveryCeiling ? discoveryCeiling : untilDue;
    }

    private void PruneStatesFor(List<WeatherSetting> enabledSettings)
    {
        if (_pollStates.Count == 0)
            return;

        var live = enabledSettings.Select(s => s.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var userId in _pollStates.Keys.Where(id => !live.Contains(id)).ToList())
            _pollStates.Remove(userId);
    }

    private TimeSpan BaseInterval(WeatherSetting setting)
        => TimeSpan.FromMinutes(Math.Max(_options.MinPollIntervalMinutes, setting.PollIntervalMinutes));

    private TimeSpan EscalatedInterval(TimeSpan baseInterval, int consecutiveFailures)
    {
        // Math.Pow saturates to infinity long before it overflows, which simply pins us to the cap.
        var minutes = baseInterval.TotalMinutes * Math.Pow(_options.FailureBackoffMultiplier, consecutiveFailures);
        var capped = Math.Min(minutes, _options.MaxFailureBackoffMinutes);
        // Never escalate a user BELOW their own configured interval (possible when that interval is
        // already longer than the cap) — backoff may only ever slow polling down.
        return TimeSpan.FromMinutes(Math.Max(capped, baseInterval.TotalMinutes));
    }

    private sealed record UserPollState(int ConsecutiveFailures, TimeSpan Interval, DateTimeOffset NextAttemptUtc);
}
