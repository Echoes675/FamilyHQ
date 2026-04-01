using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface ISettingsApiService
{
    Task<LocationSettingDto?> GetLocationAsync();
    Task<LocationSettingDto> SaveLocationAsync(string placeName);
    Task DeleteLocationAsync();
    Task<DayThemeDto> GetTodayThemeAsync();
    Task<DisplaySettingDto> GetDisplayAsync();
    Task<DisplaySettingDto> SaveDisplayAsync(DisplaySettingDto dto);
}
