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

    /// <summary>FHQ-178: report the kiosk's own OS timezone. Ignored server-side when the
    /// family has set an explicit zone.</summary>
    Task ReportKioskTimeZoneAsync(string ianaTimeZone);
}
