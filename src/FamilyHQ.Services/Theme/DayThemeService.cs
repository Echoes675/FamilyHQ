using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using NodaTime;

namespace FamilyHQ.Services.Theme;

public class DayThemeService(
    IDayThemeRepository dayThemeRepo,
    ILocationService locationService,
    ISunCalculatorService sunCalculator,
    ITimeZoneLookup timeZoneLookup,
    TimeProvider timeProvider) : IDayThemeService
{
    public async Task EnsureTodayAsync(CancellationToken ct = default)
    {
        var today = await ResolveLocalDateAsync(ct);
        var existing = await dayThemeRepo.GetByDateAsync(today, ct);
        if (existing is not null) return;

        await CalculateAndPersistAsync(today, ct);
    }

    public async Task RecalculateForTodayAsync(CancellationToken ct = default)
    {
        var today = await ResolveLocalDateAsync(ct);
        await CalculateAndPersistAsync(today, ct);
    }

    public async Task<DayThemeDto> GetTodayAsync(CancellationToken ct = default)
    {
        var today = await ResolveLocalDateAsync(ct);
        var record = await dayThemeRepo.GetByDateAsync(today, ct)
            ?? throw new InvalidOperationException("No DayTheme record found for today.");

        var localNow = ComputeLocalNow(record.IanaTimeZone);
        var currentPeriod = DeriveCurrentPeriod(record, localNow);

        return new DayThemeDto(
            record.Date,
            record.MorningStart,
            record.DaytimeStart,
            record.EveningStart,
            record.NightStart,
            record.IanaTimeZone,
            currentPeriod.ToString());
    }

    /// <summary>
    /// FHQ-134: the family's LOCAL date, which is the key every DayTheme row is stored under.
    /// <para>
    /// The circular dependency (the zone lives on the record, but the date selects the record) is
    /// broken by reading the zone off the MOST RECENT row rather than today's: one indexed read, no
    /// user context, no network. The alternatives were both dead ends. <c>ITimeZoneService</c> is
    /// per-user via <c>ICurrentUserService</c>, which reads the HTTP context — and the dominant
    /// caller, <c>DayThemeSchedulerService</c>, is a background hosted service with no HTTP context,
    /// so UserId is null there and the derivation would silently degrade to the UTC date on exactly
    /// the path this fix exists to serve. Resolving the zone live (location + lookup) would put an
    /// ip-api call on a hot path — banned, and since FHQ-114 gave that client retries it now costs up
    /// to ~12s per call. DayTheme is keyed by Date alone (no UserId), so a single global zone is the
    /// right shape here, and it is the same field <see cref="ComputeLocalNow"/> and the scheduler's
    /// boundary maths already trust.
    /// </para>
    /// <para>
    /// An empty table (first-ever boot) or an unusable stored zone falls back to the previous
    /// server-date behaviour; <see cref="CalculateAndPersistAsync"/> re-derives from the zone it
    /// resolves, so the first row still lands on the correct local date.
    /// </para>
    /// </summary>
    private async Task<DateOnly> ResolveLocalDateAsync(CancellationToken ct)
    {
        var mostRecent = await dayThemeRepo.GetMostRecentAsync(ct);
        return LocalDateIn(mostRecent?.IanaTimeZone) ?? ServerLocalDate();
    }

    private async Task CalculateAndPersistAsync(DateOnly date, CancellationToken ct)
    {
        var location = await locationService.GetEffectiveLocationAsync(ct);
        var ianaTimeZone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);

        // The zone just resolved is fresher than the one the date key was derived from — it is the
        // only thing that knows about a location change or a first-ever boot. Re-deriving here means
        // the row lands on the date that is genuinely "today" in the zone being stored alongside it.
        var resolvedDate = LocalDateIn(ianaTimeZone) ?? date;
        await PersistAsync(resolvedDate, location, ianaTimeZone, ct);

        // A zone change can move the local date. The row the NEXT date-key derivation reads its zone
        // from is the one with the greatest Date, so when the requested key is the later of the two,
        // leaving it on the abandoned zone would pin the whole app to that zone until its next
        // midnight. Refresh it too — the boundaries are correct for its own date in the new zone, and
        // the extra work is local sun maths plus one write, on a path only a relocation reaches.
        if (date > resolvedDate)
            await PersistAsync(date, location, ianaTimeZone, ct);
    }

    private async Task PersistAsync(DateOnly date, LocationResult location, string? ianaTimeZone, CancellationToken ct)
    {
        var boundaries = await sunCalculator.CalculateBoundariesAsync(
            location.Latitude, location.Longitude, date, ianaTimeZone);

        await dayThemeRepo.UpsertAsync(new DayTheme
        {
            Date = date,
            MorningStart = boundaries.MorningStart,
            DaytimeStart = boundaries.DaytimeStart,
            EveningStart = boundaries.EveningStart,
            NightStart = boundaries.NightStart,
            IanaTimeZone = ianaTimeZone
        }, ct);
    }

    private DateOnly? LocalDateIn(string? ianaTimeZone)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZone)) return null;

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone);
        if (zone is null) return null;

        var localDate = Instant.FromDateTimeOffset(timeProvider.GetUtcNow()).InZone(zone).Date;
        return new DateOnly(localDate.Year, localDate.Month, localDate.Day);
    }

    private DateOnly ServerLocalDate() => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private TimeOnly ComputeLocalNow(string? ianaTimeZone)
    {
        if (!string.IsNullOrWhiteSpace(ianaTimeZone))
        {
            var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone);
            if (zone is not null)
            {
                var instant = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
                var local = instant.InZone(zone).LocalDateTime;
                return new TimeOnly(local.Hour, local.Minute, local.Second);
            }
        }
        return TimeOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
    }

    private static TimeOfDayPeriod DeriveCurrentPeriod(DayTheme record, TimeOnly localNow)
    {
        if (localNow >= record.NightStart) return TimeOfDayPeriod.Night;
        if (localNow >= record.EveningStart) return TimeOfDayPeriod.Evening;
        if (localNow >= record.DaytimeStart) return TimeOfDayPeriod.Daytime;
        if (localNow >= record.MorningStart) return TimeOfDayPeriod.Morning;
        return TimeOfDayPeriod.Night;
    }
}
