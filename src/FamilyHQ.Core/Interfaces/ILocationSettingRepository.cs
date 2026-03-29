using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface ILocationSettingRepository
{
    Task<LocationSetting?> GetAsync(CancellationToken ct = default);
    Task<LocationSetting> UpsertAsync(LocationSetting locationSetting, CancellationToken ct = default);
}
