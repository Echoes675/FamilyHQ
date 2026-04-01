using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IDisplaySettingRepository
{
    Task<DisplaySetting?> GetAsync(CancellationToken ct = default);
    Task<DisplaySetting> UpsertAsync(DisplaySetting setting, CancellationToken ct = default);
}
