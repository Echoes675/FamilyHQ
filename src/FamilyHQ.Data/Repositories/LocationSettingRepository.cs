using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyHQ.Data.Repositories;

public class LocationSettingRepository(FamilyHqDbContext context) : ILocationSettingRepository
{
    private readonly FamilyHqDbContext _context = context;

    public async Task<LocationSetting?> GetAsync(CancellationToken ct = default)
        => await _context.LocationSettings.FirstOrDefaultAsync(ct);

    public async Task<LocationSetting> UpsertAsync(LocationSetting locationSetting, CancellationToken ct = default)
    {
        var existing = await GetAsync(ct);
        if (existing is null)
        {
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
}
