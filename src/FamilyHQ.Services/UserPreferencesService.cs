using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using FamilyHQ.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Services;

public sealed class UserPreferencesService : IUserPreferencesService
{
    private readonly FamilyHqDbContext _dbContext;

    public UserPreferencesService(FamilyHqDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<UserPreferences> GetPreferencesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var prefs = await _dbContext.UserPreferences
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        
        return prefs ?? new UserPreferences { UserId = userId };
    }

    public async Task<UserPreferences> SavePreferencesAsync(string userId, UserPreferences preferences, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.UserPreferences
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        
        if (existing is null)
        {
            preferences.UserId = userId;
            preferences.LastModified = DateTimeOffset.UtcNow;
            _dbContext.UserPreferences.Add(preferences);
        }
        else
        {
            existing.EventDensity = preferences.EventDensity;
            existing.CalendarColumnOrder = preferences.CalendarColumnOrder;
            existing.CalendarColorOverrides = preferences.CalendarColorOverrides;
            existing.LastModified = DateTimeOffset.UtcNow;
        }
        
        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing ?? preferences;
    }
}