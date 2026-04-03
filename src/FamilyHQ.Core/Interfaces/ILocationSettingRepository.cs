using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationSettingRepository
{
    /// <summary>Used by background services (no user context) — returns any location.</summary>
    Task<LocationSetting?> GetAsync(CancellationToken ct = default);

    /// <summary>Used by authenticated controller operations — scoped to the given user.</summary>
    Task<LocationSetting?> GetAsync(string userId, CancellationToken ct = default);
    Task<LocationSetting> UpsertAsync(string userId, LocationSetting locationSetting, CancellationToken ct = default);
    Task DeleteAsync(string userId, CancellationToken ct = default);
}
