using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DayThemeRepository : IDayThemeRepository
{
    private readonly FamilyHqDbContext _context;

    public DayThemeRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<DayTheme?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
        => await _context.DayThemes.FirstOrDefaultAsync(x => x.Date == date, ct);

    public async Task<DayTheme?> GetMostRecentAsync(CancellationToken ct = default)
        => await _context.DayThemes
            .AsNoTracking()
            .OrderByDescending(x => x.Date)
            .FirstOrDefaultAsync(ct);

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
        // FHQ-160: the zone travels WITH the boundaries. A same-day location change recalculates
        // today's theme, and today's row already exists, so this UPDATE branch is what runs. Dropping
        // the zone here stores the new zone's sunrise/sunset times against the OLD zone, which the
        // scheduler then reads back in the wrong zone (wrong wake instant, wrong derived period).
        existing.IanaTimeZone = dayTheme.IanaTimeZone;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
