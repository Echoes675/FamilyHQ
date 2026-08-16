using FamilyHQ.Services.Options;
using FamilyHQ.Services.Weather;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Tests.Weather;

/// <summary>
/// Exposes the protected per-cycle poll so tests can run bounded, deterministic cycles and assert the
/// per-user schedule the loop hands to <c>Task.Delay</c> — without driving the hosted-service loop
/// itself. Mirrors <c>TestableDayThemeSchedulerService</c>.
/// </summary>
internal sealed class TestableWeatherPollerService(
    IServiceProvider serviceProvider,
    IOptions<WeatherOptions> options,
    TimeProvider timeProvider,
    ILogger<WeatherPollerService> logger)
    : WeatherPollerService(serviceProvider, options, timeProvider, logger)
{
    public Task<TimeSpan> RunCycleAsync(CancellationToken ct) => RunPollCycleAsync(ct);
}
