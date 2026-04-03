namespace FamilyHQ.Core.Interfaces;

using FamilyHQ.Core.Models;

public interface IWeatherSettingRepository
{
    Task<WeatherSetting> GetOrCreateAsync(CancellationToken ct = default);
    Task<WeatherSetting> UpsertAsync(WeatherSetting setting, CancellationToken ct = default);
}
