using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDisplaySettingRepository
{
    Task<DisplaySetting?> GetAsync(string userId, CancellationToken ct = default);
    Task<DisplaySetting> UpsertAsync(string userId, DisplaySetting setting, CancellationToken ct = default);
}
