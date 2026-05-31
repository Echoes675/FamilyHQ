using FamilyHQ.Core.Interfaces;
using GeoTimeZone;
using Microsoft.Extensions.Caching.Memory;
using NodaTime;
using NodaTime.Text;

namespace FamilyHQ.Services.Calendar;

public class TimeZoneService(
    ICurrentUserService currentUser,
    IDisplaySettingRepository displayRepo,
    ILocationSettingRepository locationRepo,
    ILocationService locationService,
    IMemoryCache cache) : ITimeZoneService
{
    private static readonly LocalDateTimePattern Pattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm:ss");

    public async Task<string?> GetEffectiveIanaZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        // 1. Explicit per-user setting (sticky, independent of location).
        var display = await displayRepo.GetAsync(userId, ct);
        if (!string.IsNullOrWhiteSpace(display?.IanaTimeZone) && IsValidZone(display.IanaTimeZone))
            return display.IanaTimeZone;

        // 2. Custom location -> derive from its lat/lon (GeoTimeZone, bundled data).
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is not null)
        {
            var derived = TimeZoneLookup.GetTimeZone(location.Latitude, location.Longitude).Result;
            if (!string.IsNullOrWhiteSpace(derived) && IsValidZone(derived)) return derived;
        }

        // 3. Auto: ip-api timezone (cached per user — tz rarely changes; avoids a lookup per create).
        return await cache.GetOrCreateAsync($"ipapi-tz:{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            try
            {
                var auto = await locationService.GetEffectiveLocationAsync(ct);
                return !string.IsNullOrWhiteSpace(auto.IanaTimeZone) && IsValidZone(auto.IanaTimeZone) ? auto.IanaTimeZone : null;
            }
            catch { return null; } // never fail an event create on a tz lookup
        });
    }

    public string ToZonedWallClock(DateTimeOffset utcInstant, string ianaZone)
    {
        var zone = DateTimeZoneProviders.Tzdb[ianaZone];
        var local = Instant.FromDateTimeOffset(utcInstant).InZone(zone).LocalDateTime;
        return Pattern.Format(local);
    }

    public bool IsValidZone(string ianaZone) => DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaZone) is not null;

    public IReadOnlyList<string> GetAvailableZoneIds() => DateTimeZoneProviders.Tzdb.Ids;
}
