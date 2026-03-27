namespace FamilyHQ.WebUi.Services;

using FamilyHQ.WebUi.Models;

public interface IUserPreferencesService
{
    UserPreferencesDto Current { get; }
    event EventHandler<UserPreferencesDto>? PreferencesChanged;
    Task LoadAsync();
    Task SaveAsync(UserPreferencesDto preferences);
    Task UpdateEventDensityAsync(int density);
    Task UpdateCalendarOrderAsync(List<string> calendarIds);
    Task UpdateCalendarColorAsync(string calendarId, string hexColor);
}