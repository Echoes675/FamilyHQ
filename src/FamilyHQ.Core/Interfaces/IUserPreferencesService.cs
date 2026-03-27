using FamilyHQ.Core.Models;

namespace FamilyHQ.Core.Interfaces;

public interface IUserPreferencesService
{
    Task<UserPreferences> GetPreferencesAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserPreferences> SavePreferencesAsync(string userId, UserPreferences preferences, CancellationToken cancellationToken = default);
}