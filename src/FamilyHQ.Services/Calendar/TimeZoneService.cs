using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using GeoTimeZone;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;

namespace FamilyHQ.Services.Calendar;

public class TimeZoneService(
    ICurrentUserService currentUser,
    IDisplaySettingRepository displayRepo,
    ILocationSettingRepository locationRepo,
    ILocationService locationService,
    IServiceScopeFactory scopeFactory) : ITimeZoneService
{
    private static readonly LocalDateTimePattern Pattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm:ss");

    public async Task<string?> ResolveAutoZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        // Saved custom location -> derive from its lat/lon (GeoTimeZone, bundled data). NO ip-api.
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is not null)
        {
            var derived = TimeZoneLookup.GetTimeZone(location.Latitude, location.Longitude).Result;
            if (!string.IsNullOrWhiteSpace(derived) && IsValidZone(derived)) return derived;
        }

        // No saved location -> auto-detect via ip-api (single call returns location AND timezone).
        // Never let a tz lookup throw — return null on any failure.
        try
        {
            var auto = await locationService.GetEffectiveLocationAsync(ct);
            if (!string.IsNullOrWhiteSpace(auto.IanaTimeZone) && IsValidZone(auto.IanaTimeZone))
                return auto.IanaTimeZone;
        }
        catch { /* never fail a resolve on a tz lookup */ }

        return null;
    }

    public async Task<string?> GetSendZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        var display = await displayRepo.GetAsync(userId, ct);
        if (!string.IsNullOrWhiteSpace(display?.IanaTimeZone))
            return display.IanaTimeZone;

        // Unset -> lazily resolve the AUTO zone ONCE and cache it. GetSendZoneAsync runs INSIDE
        // outbound event writes (Create/UpdateEventAsync), which execute mid-way through event/member
        // operations holding uncommitted changes on the request-scoped DbContext. Persisting on that
        // shared context would SaveChanges and flush those in-flight member changes early (FHQ-43
        // regression: membership silently dropped). Persist on a SEPARATE scope so this is a
        // side-effect-free cache write that cannot disturb the caller's transaction.
        var resolved = await ResolveAutoZoneAsync(ct);
        if (resolved is null) return null;

        await PersistAutoZoneInSeparateScopeAsync(userId, resolved, ct);
        return resolved;
    }

    public async Task SetExplicitZoneAsync(string ianaZone, CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        var display = await displayRepo.GetAsync(userId, ct);
        await PersistZoneAsync(userId, display, ianaZone, isAutoDetected: false, ct);
    }

    public async Task ResetToAutoZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        var resolved = await ResolveAutoZoneAsync(ct);
        var display = await displayRepo.GetAsync(userId, ct);
        await PersistZoneAsync(userId, display, resolved, isAutoDetected: true, ct);
    }

    public async Task RepersistAutoIfNotExplicitAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return;

        var display = await displayRepo.GetAsync(userId, ct);

        // Explicit (zone set and NOT auto-detected) is sticky across location changes -> no-op.
        if (display is not null
            && !string.IsNullOrWhiteSpace(display.IanaTimeZone)
            && !display.IsTimeZoneAutoDetected)
        {
            return;
        }

        var resolved = await ResolveAutoZoneAsync(ct);
        await PersistZoneAsync(userId, display, resolved, isAutoDetected: true, ct);
    }

    private async Task PersistZoneAsync(
        string userId, DisplaySetting? existing, string? ianaZone, bool isAutoDetected, CancellationToken ct)
    {
        var setting = existing ?? new DisplaySetting
        {
            UserId = userId,
            SurfaceMultiplier = 1.0,
            OpaqueSurfaces = false,
            TransitionDurationSecs = 15,
            ThemeSelection = "auto"
        };
        setting.IanaTimeZone = ianaZone;
        setting.IsTimeZoneAutoDetected = isAutoDetected;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await displayRepo.UpsertAsync(userId, setting, ct);
    }

    // FHQ-43: persist the lazily-resolved auto zone on a FRESH DbContext scope (see GetSendZoneAsync)
    // so this cache write never shares the request's change tracker / SaveChanges and so cannot flush
    // a caller's in-flight event/member changes mid-operation.
    private async Task PersistAutoZoneInSeparateScopeAsync(string userId, string zone, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDisplaySettingRepository>();
        var existing = await repo.GetAsync(userId, ct);
        var setting = existing ?? new DisplaySetting
        {
            UserId = userId,
            SurfaceMultiplier = 1.0,
            OpaqueSurfaces = false,
            TransitionDurationSecs = 15,
            ThemeSelection = "auto"
        };
        setting.IanaTimeZone = zone;
        setting.IsTimeZoneAutoDetected = true;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await repo.UpsertAsync(userId, setting, ct);
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
