using FamilyHQ.Core.DTOs;
using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;

namespace FamilyHQ.Services.Theme;

public class DayThemeService(
    IDayThemeRepository dayThemeRepo,
    ILocationService locationService,
    ISunCalculatorService sunCalculator) : IDayThemeService
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

        var currentPeriod = DeriveCurrentPeriod(record);

        return new DayThemeDto(
            record.Date,
            record.MorningStart,
            record.DaytimeStart,
            record.EveningStart,
            record.NightStart,
            currentPeriod.ToString());
    }

    private async Task CalculateAndPersistAsync(DateOnly date, CancellationToken ct)
    {
        var location = await locationService.GetEffectiveLocationAsync(ct);
        var boundaries = await sunCalculator.CalculateBoundariesAsync(location.Latitude, location.Longitude, date);

        await dayThemeRepo.UpsertAsync(new DayTheme
        {
            Date = date,
            MorningStart = boundaries.MorningStart,
            DaytimeStart = boundaries.DaytimeStart,
            EveningStart = boundaries.EveningStart,
            NightStart = boundaries.NightStart
        }, ct);
    }

    private static TimeOfDayPeriod DeriveCurrentPeriod(DayTheme record)
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        if (now >= record.NightStart) return TimeOfDayPeriod.Night;
        if (now >= record.EveningStart) return TimeOfDayPeriod.Evening;
        if (now >= record.DaytimeStart) return TimeOfDayPeriod.Daytime;
        if (now >= record.MorningStart) return TimeOfDayPeriod.Morning;
        return TimeOfDayPeriod.Night;
    }
}
