namespace FamilyHQ.Data.Repositories;

using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

public class WeatherSettingRepository(FamilyHqDbContext context) : IWeatherSettingRepository
{
    public async Task<WeatherSetting> GetOrCreateAsync(CancellationToken ct = default)
    {
        var setting = await context.WeatherSettings.FirstOrDefaultAsync(ct);
        if (setting is not null)
            return setting;

        setting = new WeatherSetting();
        context.WeatherSettings.Add(setting);
        await context.SaveChangesAsync(ct);
        return setting;
    }

    public async Task<WeatherSetting> UpsertAsync(WeatherSetting setting, CancellationToken ct = default)
    {
        var existing = await context.WeatherSettings.FirstOrDefaultAsync(ct);
        if (existing is null)
        {
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
}
