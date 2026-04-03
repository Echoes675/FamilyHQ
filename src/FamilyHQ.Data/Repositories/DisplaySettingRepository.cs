using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DisplaySettingRepository : IDisplaySettingRepository
{
    private readonly FamilyHqDbContext _context;

    public DisplaySettingRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<DisplaySetting?> GetAsync(string userId, CancellationToken ct = default)
        => await _context.DisplaySettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<DisplaySetting> UpsertAsync(string userId, DisplaySetting setting, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is null)
        {
            setting.UserId = userId;
            _context.DisplaySettings.Add(setting);
            await _context.SaveChangesAsync(ct);
            return setting;
        }

        existing.SurfaceMultiplier = setting.SurfaceMultiplier;
        existing.OpaqueSurfaces = setting.OpaqueSurfaces;
        existing.TransitionDurationSecs = setting.TransitionDurationSecs;
        existing.ThemeSelection = setting.ThemeSelection;
        existing.UpdatedAt = setting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
