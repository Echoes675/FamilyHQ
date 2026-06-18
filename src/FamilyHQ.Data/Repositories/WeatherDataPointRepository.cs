namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

public class WeatherDataPointRepository(FamilyHqDbContext context, TimeProvider timeProvider) : IWeatherDataPointRepository
{
    // timeProvider is used in Task 4 for GetDailyAsync; stored here to avoid CS9113.
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default)
        => await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId && x.DataType == WeatherDataType.Current)
            .OrderByDescending(x => x.RetrievedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date,
        string? ianaTimeZone, CancellationToken ct = default)
    {
        var (start, end) = ToUtcRange(date, ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null);

        return await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId
                && x.DataType == WeatherDataType.Hourly
                && x.Timestamp >= start
                && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);
    }

    public async Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days, CancellationToken ct = default)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var end = today.AddDays(days);

        return await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId
                && x.DataType == WeatherDataType.Daily
                && x.Timestamp >= today
                && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);
    }

    public async Task ReplaceAllAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default)
    {
        // Set-based delete (one statement, no per-row params) instead of loading + RemoveRange,
        // which produced a multi-thousand-parameter DELETE whose EF command-log event exceeded
        // Seq's 256 KB limit (FHQ-52). The transaction wraps the delete + insert for atomicity,
        // since ExecuteDeleteAsync runs immediately, outside SaveChanges.
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.WeatherDataPoints
            .Where(x => x.LocationSettingId == locationSettingId)
            .ExecuteDeleteAsync(ct);
        context.WeatherDataPoints.AddRange(dataPoints);
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static (DateTimeOffset Start, DateTimeOffset End) ToUtcRange(DateOnly date, DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var localDate = new LocalDate(date.Year, date.Month, date.Day);
            var start = zone.AtStartOfDay(localDate).ToDateTimeOffset();
            var end   = zone.AtStartOfDay(localDate.PlusDays(1)).ToDateTimeOffset();
            return (start, end);
        }
        var utcStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (utcStart, utcStart.AddDays(1));
    }
}
