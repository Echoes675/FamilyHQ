using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class DisplaySettingRepository(FamilyHqDbContext context) : IDisplaySettingRepository
{
    public async Task<DisplaySetting?> GetAsync(string userId, CancellationToken ct = default)
        => await context.DisplaySettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<DisplaySetting> UpsertAsync(string userId, DisplaySetting setting, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is null)
        {
            setting.UserId = userId;
            context.DisplaySettings.Add(setting);
            await context.SaveChangesAsync(ct);
            return setting;
        }

        existing.SurfaceMultiplier = setting.SurfaceMultiplier;
        existing.OpaqueSurfaces = setting.OpaqueSurfaces;
        existing.TransitionDurationSecs = setting.TransitionDurationSecs;
        existing.ThemeSelection = setting.ThemeSelection;
        existing.UpdatedAt = setting.UpdatedAt;
        await context.SaveChangesAsync(ct);
        return existing;
    }
}
