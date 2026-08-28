using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using NodaTime;

namespace FamilyHQ.Services.Theme;

public class DayThemeService(
    IDayThemeRepository dayThemeRepo,
    ILocationSettingRepository locationRepo,
    ISunCalculatorService sunCalculator,
    ITimeZoneLookup timeZoneLookup,
    TimeProvider timeProvider) : IDayThemeService
{
    public async Task EnsureTodayAsync(string userId, CancellationToken ct = default)
    {
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is null) return;

        var zone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
        var today = LocalDateIn(zone) ?? ServerLocalDate();

        var existing = await dayThemeRepo.GetByDateAsync(userId, today, ct);
        if (existing is not null) return;

        await PersistAsync(userId, location, zone, today, ct);
    }

    public async Task RecalculateForTodayAsync(string userId, CancellationToken ct = default)
    {
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is null) return;

        var zone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
        var today = LocalDateIn(zone) ?? ServerLocalDate();

        await PersistAsync(userId, location, zone, today, ct);
    }

    public async Task<DayThemeDto?> GetTodayAsync(string userId, CancellationToken ct = default)
    {
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is null) return null;

        var zone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
        var today = LocalDateIn(zone) ?? ServerLocalDate();

        var record = await dayThemeRepo.GetByDateAsync(userId, today, ct);
        if (record is null) return null;

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
    /// FHQ-177: the kiosk's LOCAL date, which is the date half of every row's key.
    /// <para>
    /// This used to be circular — the zone lived on the DayTheme row, but the date selected the row —
    /// and FHQ-134 broke the cycle by reading the zone off the most recent row. Scoping the theme to
    /// the kiosk removes the cycle instead of working around it: the zone now comes from the kiosk's
    /// saved location, which is known before the date is needed. That is why
    /// <c>GetMostRecentAsync</c> is gone.
    /// </para>
    /// </summary>
    private DateOnly? LocalDateIn(string? ianaTimeZone)
    {
        if (string.IsNullOrWhiteSpace(ianaTimeZone)) return null;

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaTimeZone);
        if (zone is null) return null;

        var localDate = Instant.FromDateTimeOffset(timeProvider.GetUtcNow()).InZone(zone).Date;
        return new DateOnly(localDate.Year, localDate.Month, localDate.Day);
    }

    private DateOnly ServerLocalDate() => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    private async Task PersistAsync(
        string userId, LocationSetting location, string? ianaTimeZone, DateOnly date, CancellationToken ct)
    {
        var boundaries = await sunCalculator.CalculateBoundariesAsync(
            location.Latitude, location.Longitude, date, ianaTimeZone);

        await dayThemeRepo.UpsertAsync(new DayTheme
        {
            UserId = userId,
            Date = date,
            MorningStart = boundaries.MorningStart,
            DaytimeStart = boundaries.DaytimeStart,
            EveningStart = boundaries.EveningStart,
            NightStart = boundaries.NightStart,
            IanaTimeZone = ianaTimeZone
        }, ct);
    }

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
