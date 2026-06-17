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
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existing = await dayThemeRepo.GetByDateAsync(today, ct);
        if (existing is not null) return;

        await CalculateAndPersistAsync(today, ct);
    }

    public async Task RecalculateForTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        await CalculateAndPersistAsync(today, ct);
    }

    public async Task<DayThemeDto> GetTodayAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
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

    private async Task CalculateAndPersistAsync(DateOnly date, CancellationToken ct)
    {
        var location = await locationService.GetEffectiveLocationAsync(ct);
        var ianaTimeZone = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
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

    private TimeOnly ComputeLocalNow(string? ianaTimeZone)
    {
        if (!string.IsNullOrWhiteSpace(ianaTimeZone))
        {
            var zone = DateTimeZoneProviders.Tzdb[ianaTimeZone];
            var instant = Instant.FromDateTimeOffset(timeProvider.GetUtcNow());
            var local = instant.InZone(zone).LocalDateTime;
            return new TimeOnly(local.Hour, local.Minute, local.Second);
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
