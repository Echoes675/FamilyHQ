using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IWeatherSettingRepository
{
    Task<WeatherSetting> GetOrCreateAsync(string userId, CancellationToken ct = default);
    Task<WeatherSetting> UpsertAsync(string userId, WeatherSetting setting, CancellationToken ct = default);
    Task<List<WeatherSetting>> GetAllAsync(CancellationToken ct = default);
}
