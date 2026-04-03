namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

public class WeatherDataPointRepository(FamilyHqDbContext context) : IWeatherDataPointRepository
{
    public async Task<WeatherDataPoint?> GetCurrentAsync(int locationSettingId, CancellationToken ct = default)
        => await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId && x.DataType == WeatherDataType.Current)
            .OrderByDescending(x => x.RetrievedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<List<WeatherDataPoint>> GetHourlyAsync(int locationSettingId, DateOnly date, CancellationToken ct = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

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
        var existing = await context.WeatherDataPoints
            .Where(x => x.LocationSettingId == locationSettingId)
            .ToListAsync(ct);

        context.WeatherDataPoints.RemoveRange(existing);
        context.WeatherDataPoints.AddRange(dataPoints);
        await context.SaveChangesAsync(ct);
    }
}
