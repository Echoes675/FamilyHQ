using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class WeatherSettingRepository(FamilyHqDbContext context) : IWeatherSettingRepository
{
    public async Task<WeatherSetting> GetOrCreateAsync(string userId, CancellationToken ct = default)
    {
        var setting = await context.WeatherSettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (setting is not null)
            return setting;

        setting = new WeatherSetting { UserId = userId };
        context.WeatherSettings.Add(setting);
        await context.SaveChangesAsync(ct);
        return setting;
    }

    public async Task<WeatherSetting> UpsertAsync(string userId, WeatherSetting setting, CancellationToken ct = default)
    {
        var existing = await context.WeatherSettings
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is null)
        {
            setting.UserId = userId;
            context.WeatherSettings.Add(setting);
            await context.SaveChangesAsync(ct);
            return setting;
        }

        existing.Enabled = setting.Enabled;
        existing.PollIntervalMinutes = setting.PollIntervalMinutes;
        existing.TemperatureUnit = setting.TemperatureUnit;
        existing.WindThresholdKmh = setting.WindThresholdKmh;
        existing.ApiKey = setting.ApiKey;
        await context.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<List<WeatherSetting>> GetAllAsync(CancellationToken ct = default)
        => await context.WeatherSettings.AsNoTracking().ToListAsync(ct);
}
