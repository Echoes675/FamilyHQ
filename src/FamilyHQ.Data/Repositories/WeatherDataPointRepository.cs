namespace FamilyHQ.Data.Repositories;

using System.Linq.Expressions;
using FamilyHQ.Core.Enums;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

public class WeatherDataPointRepository(FamilyHqDbContext context, TimeProvider timeProvider) : IWeatherDataPointRepository
{
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

    public async Task<List<WeatherDataPoint>> GetDailyAsync(int locationSettingId, int days,
        string? ianaTimeZone, CancellationToken ct = default)
    {
        var zone = ianaTimeZone is not null
            ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone)
            : null;

        DateTimeOffset start, end;
        if (zone is not null)
        {
            var instant = Instant.FromDateTimeOffset(_timeProvider.GetUtcNow());
            var localToday = instant.InZone(zone).Date;
            // Npgsql requires UTC-offset DateTimeOffset for timestamptz parameters; ToUniversalTime() preserves the instant.
            start = zone.AtStartOfDay(localToday).ToDateTimeOffset().ToUniversalTime();
            end   = zone.AtStartOfDay(localToday.PlusDays(days)).ToDateTimeOffset().ToUniversalTime();
        }
        else
        {
            var utcNow = _timeProvider.GetUtcNow();
            start = new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, TimeSpan.Zero);
            end   = start.AddDays(days);
        }

        return await context.WeatherDataPoints
            .AsNoTracking()
            .Where(x => x.LocationSettingId == locationSettingId
                && x.DataType == WeatherDataType.Daily
                && x.Timestamp >= start
                && x.Timestamp < end)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);
    }

    public async Task ReplaceSectionsAsync(int locationSettingId, List<WeatherDataPoint> dataPoints, CancellationToken ct = default)
    {
        var sections = SectionsReplacedBy(dataPoints);

        // Pure optimisation, not a behaviour: an empty section list already matches no rows, so
        // this only avoids opening a transaction to run a delete that can never match.
        if (sections.Count == 0)
            return;

        // Set-based delete (one statement, no per-row params) instead of loading + RemoveRange,
        // which produced a multi-thousand-parameter DELETE whose EF command-log event exceeded
        // Seq's 256 KB limit (FHQ-52). The transaction wraps the delete + insert for atomicity,
        // since ExecuteDeleteAsync runs immediately, outside SaveChanges.
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.WeatherDataPoints
            .Where(RowsReplacedBy(locationSettingId, sections))
            .ExecuteDeleteAsync(ct);
        context.WeatherDataPoints.AddRange(dataPoints);
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// The sections a refresh replaces: exactly those its payload carried rows for. A section whose
    /// Open-Meteo block came back empty contributes no rows, so it is not replaced — and a payload
    /// carrying nothing at all replaces nothing (FHQ-159).
    /// </summary>
    public static List<WeatherDataType> SectionsReplacedBy(List<WeatherDataPoint> dataPoints) =>
        [.. dataPoints.Select(x => x.DataType).Distinct()];

    /// <summary>
    /// The stored rows that a refresh carrying <paramref name="sections"/> replaces: this location's
    /// rows in those sections and no others.
    /// <para>
    /// FHQ-159: the predicate used to match on <c>LocationSettingId</c> alone, so a payload whose
    /// <c>hourly</c> block came back empty wiped the stored hourly rows as a side effect of
    /// rewriting <c>daily</c> — one degraded Open-Meteo response blanked forecast the kiosk was
    /// showing correctly. Narrowing it to the sections actually carried is the whole fix; it stays a
    /// single set-based statement (<c>data_type = ANY(...)</c>), preserving the FHQ-52 shape.
    /// </para>
    /// Public because it IS the retention rule, and asserting it must not require a database.
    /// </summary>
    public static Expression<Func<WeatherDataPoint, bool>> RowsReplacedBy(
        int locationSettingId, List<WeatherDataType> sections) =>
        x => x.LocationSettingId == locationSettingId && sections.Contains(x.DataType);

    private static (DateTimeOffset Start, DateTimeOffset End) ToUtcRange(DateOnly date, DateTimeZone? zone)
    {
        if (zone is not null)
        {
            var localDate = new LocalDate(date.Year, date.Month, date.Day);
            // Npgsql requires UTC-offset DateTimeOffset for timestamptz parameters; ToUniversalTime() preserves the instant.
            var start = zone.AtStartOfDay(localDate).ToDateTimeOffset().ToUniversalTime();
            var end   = zone.AtStartOfDay(localDate.PlusDays(1)).ToDateTimeOffset().ToUniversalTime();
            return (start, end);
        }
        var utcStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (utcStart, utcStart.AddDays(1));
    }
}
