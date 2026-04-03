using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class LocationSettingRepository : ILocationSettingRepository
{
    private readonly FamilyHqDbContext _context;

    public LocationSettingRepository(FamilyHqDbContext context)
    {
        _context = context;
    }

    public async Task<LocationSetting?> GetAsync(CancellationToken ct = default)
        => await _context.LocationSettings.FirstOrDefaultAsync(ct);

    public async Task<LocationSetting?> GetAsync(string userId, CancellationToken ct = default)
        => await _context.LocationSettings.FirstOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<LocationSetting> UpsertAsync(string userId, LocationSetting locationSetting, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is null)
        {
            locationSetting.UserId = userId;
            _context.LocationSettings.Add(locationSetting);
            await _context.SaveChangesAsync(ct);
            return locationSetting;
        }

        existing.PlaceName = locationSetting.PlaceName;
        existing.Latitude = locationSetting.Latitude;
        existing.Longitude = locationSetting.Longitude;
        existing.UpdatedAt = locationSetting.UpdatedAt;
        await _context.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteAsync(string userId, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is not null)
        {
            _context.LocationSettings.Remove(existing);
            await _context.SaveChangesAsync(ct);
        }
    }
}
