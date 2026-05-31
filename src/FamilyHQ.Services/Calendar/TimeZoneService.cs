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
        //    Only positive results are cached; a failure returns null without poisoning the cache.
        var cacheKey = $"ipapi-tz:{userId}";
        if (cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        string? resolved = null;
        try
        {
            var auto = await locationService.GetEffectiveLocationAsync(ct);
            if (!string.IsNullOrWhiteSpace(auto.IanaTimeZone) && IsValidZone(auto.IanaTimeZone))
                resolved = auto.IanaTimeZone;
        }
        catch { /* never fail an event create on a tz lookup */ }

        if (resolved is not null)
            cache.Set(cacheKey, resolved, TimeSpan.FromHours(6));

        return resolved;
    }

    public string ToZonedWallClock(DateTimeOffset utcInstant, string ianaZone)
    {
        if (!IsValidZone(ianaZone))
            throw new ArgumentException($"Unknown IANA timezone '{ianaZone}'.", nameof(ianaZone));
        var zone = DateTimeZoneProviders.Tzdb[ianaZone];
        var local = Instant.FromDateTimeOffset(utcInstant).InZone(zone).LocalDateTime;
        return Pattern.Format(local);
    }

    public bool IsValidZone(string ianaZone) => DateTimeZoneProviders.Tzdb.GetZoneOrNull(ianaZone) is not null;

    public IReadOnlyList<string> GetAvailableZoneIds() => DateTimeZoneProviders.Tzdb.Ids;
}
