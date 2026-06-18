using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Services.Options;
using FamilyHQ.Services.Theme;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Tests.Theme;

/// <summary>
/// Exposes the protected <see cref="Microsoft.Extensions.Hosting.BackgroundService.ExecuteAsync"/>
/// loop so tests can drive a single bounded run (cancelling the token to break the loop) and assert
/// that failures inside the loop never escape to the host.
/// </summary>
internal sealed class TestableDayThemeSchedulerService(
    IServiceProvider serviceProvider,
    IThemeBroadcaster themeBroadcaster,
    ILogger<DayThemeSchedulerService> logger,
    IOptions<DayThemeOptions> options,
    TimeProvider timeProvider)
    : DayThemeSchedulerService(serviceProvider, themeBroadcaster, logger, options, timeProvider)
{
    public Task RunExecuteAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);
    public TimeSpan TestGetNextBoundaryDelay(DayThemeDto dto) => GetNextBoundaryDelay(dto);
}
