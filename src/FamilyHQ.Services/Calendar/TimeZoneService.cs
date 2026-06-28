using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using NodaTime;
using NodaTime.Text;

namespace FamilyHQ.Services.Calendar;

public class TimeZoneService(
    ICurrentUserService currentUser,
    IDisplaySettingRepository displayRepo,
    ILocationSettingRepository locationRepo,
    ILocationService locationService,
    ITimeZoneLookup timeZoneLookup) : ITimeZoneService
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
            var derived = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
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

        // READ-ONLY. The effective zone is persisted ONCE at a change point: location auto-discovery
        // (SettingsController.GetLocation -> EnsureAutoZonePersistedAsync), manual set, or location
        // save/reset. The outbound Google-write path must NEVER resolve here — doing so would call
        // ip-api on every write AND write the request-scoped DbContext mid event/member operation
        // (which shifted sync timing and re-exposed the membership flap). Unset -> null (caller -> UTC).
        var display = await displayRepo.GetAsync(userId, ct);
        return string.IsNullOrWhiteSpace(display?.IanaTimeZone) ? null : display.IanaTimeZone;
    }

    public async Task EnsureAutoZonePersistedAsync(string? ianaZone, CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || string.IsNullOrWhiteSpace(ianaZone) || !IsValidZone(ianaZone))
            return;

        var display = await displayRepo.GetAsync(userId, ct);
        // Auto-discovery sets the zone ONCE. If the user already has a zone (auto or explicit), leave it.
        if (!string.IsNullOrWhiteSpace(display?.IanaTimeZone)) return;

        await PersistZoneAsync(userId, display, ianaZone, isAutoDetected: true, ct);
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
