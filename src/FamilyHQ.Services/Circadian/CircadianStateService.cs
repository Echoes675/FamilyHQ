using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using FamilyHQ.Services.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHQ.Services.Circadian;

/// <summary>
/// Background service that computes circadian boundaries daily at midnight
/// using the NOAA solar algorithm and stores them in the database.
/// </summary>
public sealed class CircadianStateService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISolarCalculator _solarCalculator;
    private readonly KioskOptions _kioskOptions;
    private readonly ILogger<CircadianStateService> _logger;

    public CircadianStateService(
        IServiceScopeFactory scopeFactory,
        ISolarCalculator solarCalculator,
        IOptions<KioskOptions> kioskOptions,
        ILogger<CircadianStateService> logger)
    {
        _scopeFactory = scopeFactory;
        _solarCalculator = solarCalculator;
        _kioskOptions = kioskOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CircadianStateService starting");
        
        // Compute for today on startup
        await ComputeAndStoreAsync(DateOnly.FromDateTime(DateTime.UtcNow), stoppingToken);
        // Also compute for tomorrow (so it's ready at midnight)
        await ComputeAndStoreAsync(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), stoppingToken);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait until next midnight UTC
            var now = DateTime.UtcNow;
            var nextMidnight = now.Date.AddDays(1);
            var delay = nextMidnight - now;
            
            await Task.Delay(delay, stoppingToken);
            
            if (!stoppingToken.IsCancellationRequested)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                await ComputeAndStoreAsync(today, stoppingToken);
                await ComputeAndStoreAsync(today.AddDays(1), stoppingToken);
            }
        }
    }

    private async Task ComputeAndStoreAsync(DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var result = _solarCalculator.Calculate(date, _kioskOptions.Latitude, _kioskOptions.Longitude);
            if (result is null)
            {
                _logger.LogWarning("Solar calculator returned null for {Date} (polar region?)", date);
                return;
            }

            var (sunrise, sunset) = result.Value;
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FamilyHqDbContext>();
            
            // Upsert: update if exists, insert if not
            var existing = await dbContext.CircadianBoundaries
                .FirstOrDefaultAsync(x => x.Date == date 
                    && x.Latitude == _kioskOptions.Latitude 
                    && x.Longitude == _kioskOptions.Longitude, cancellationToken);
            
            if (existing is not null)
            {
                existing.SunriseUtc = sunrise;
                existing.SunsetUtc = sunset;
                existing.ComputedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                dbContext.CircadianBoundaries.Add(new CircadianBoundaries
                {
                    Date = date,
                    Latitude = _kioskOptions.Latitude,
                    Longitude = _kioskOptions.Longitude,
                    SunriseUtc = sunrise,
                    SunsetUtc = sunset,
                    ComputedAt = DateTimeOffset.UtcNow
                });
            }
            
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Circadian boundaries computed for {Date}: sunrise={Sunrise}, sunset={Sunset}", 
                date, sunrise, sunset);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to compute circadian boundaries for {Date}", date);
        }
    }
}
