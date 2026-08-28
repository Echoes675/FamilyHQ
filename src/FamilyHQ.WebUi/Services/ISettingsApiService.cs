using FamilyHQ.Core.DTOs;

namespace FamilyHQ.WebUi.Services;

public interface ISettingsApiService
{
    Task<LocationSettingDto?> GetLocationAsync();
    Task<LocationSettingDto> SaveLocationAsync(string placeName);
    Task DeleteLocationAsync();
    /// <summary>Null when this kiosk has no saved location, so there is no theme to show.</summary>
    Task<DayThemeDto?> GetTodayThemeAsync();
    Task<DisplaySettingDto> GetDisplayAsync();
    Task<DisplaySettingDto> SaveDisplayAsync(DisplaySettingDto dto);
    Task<IReadOnlyList<string>> GetTimeZonesAsync();
    Task<TimeZoneSettingDto> GetTimeZoneAsync();
    Task SetTimeZoneAsync(string ianaTimeZone);
    Task ResetTimeZoneAsync();
}
