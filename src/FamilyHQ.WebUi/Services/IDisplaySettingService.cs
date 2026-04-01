using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface IDisplaySettingService : IAsyncDisposable
{
    Task InitialiseAsync();
    Task UpdatePropertyAsync(string cssPropertyName, string value);
    Task SaveAsync(DisplaySettingDto dto);
    DisplaySettingDto CurrentSettings { get; }
}
