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

    public async Task<DisplaySetting?> GetAsync(CancellationToken ct = default)
        => await _context.DisplaySettings.FirstOrDefaultAsync(ct);

    public async Task<DisplaySetting> UpsertAsync(DisplaySetting setting, CancellationToken ct = default)
    {
        var existing = await GetAsync(ct);
        if (existing is null)
        {
            _context.DisplaySettings.Add(setting);
            await _context.SaveChangesAsync(ct);
            return setting;
        }

        existing.SurfaceMultiplier = setting.SurfaceMultiplier;
        existing.OpaqueSurfaces = setting.OpaqueSurfaces;
        existing.TransitionDurationSecs = setting.TransitionDurationSecs;
        existing.UpdatedAt = setting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }
}
