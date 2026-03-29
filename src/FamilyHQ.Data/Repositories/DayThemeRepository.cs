using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DayThemeRepository(FamilyHqDbContext context) : IDayThemeRepository
{
    private readonly FamilyHqDbContext _context = context;

    public async Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => await _context.DayThemes.FirstOrDefaultAsync(x => x.Date == date, ct);

    public async Task<DayTheme> UpsertAsync(DayTheme dayTheme, CancellationToken ct = default)
    {
        var existing = await GetByDateAsync(dayTheme.Date, ct);
        if (existing is null)
        {
            _context.DayThemes.Add(dayTheme);
            await _context.SaveChangesAsync(ct);
            return dayTheme;
        }

        existing.MorningStart = dayTheme.MorningStart;
        existing.DaytimeStart = dayTheme.DaytimeStart;
        existing.EveningStart = dayTheme.EveningStart;
        existing.NightStart = dayTheme.NightStart;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
