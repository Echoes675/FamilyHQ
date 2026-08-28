using FamilyHQ.Core.Interfaces;
using FamilyHQ.Core.Models;
using NodaTime;
using NodaTime.Text;

namespace FamilyHQ.Services.Calendar;

public class TimeZoneService(
    ICurrentUserService currentUser,
    IDisplaySettingRepository displayRepo,
    ILocationSettingRepository locationRepo,
    ITimeZoneLookup timeZoneLookup) : ITimeZoneService
{
    private static readonly LocalDateTimePattern Pattern =
        LocalDateTimePattern.CreateWithInvariantCulture("uuuu-MM-dd'T'HH:mm:ss");

    public async Task<string?> ResolveAutoZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        // Saved custom location -> derive from its lat/lon (GeoTimeZone, bundled data).
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is not null)
        {
            var derived = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
            if (!string.IsNullOrWhiteSpace(derived) && IsValidZone(derived)) return derived;
        }

        // FHQ-178: there is deliberately NO ip-api fallback here any more. That call is made from the
        // WebApi container, so it geolocates the HOSTING VPS rather than the family — production
        // returned Europe/Berlin for a household in Derry. It is not unlucky, it is structural: no
        // server-side IP lookup can identify where the family lives.
        //
        // This value reaches Google. GetSendZoneAsync feeds `familyZone` on event creation, so a
        // guessed zone here is stamped onto the family's calendar and read back on their phones —
        // the incidental change AGENTS.md's golden rule prohibits. The kiosk reports its own OS zone
        // instead (SetKioskZoneAsync); if it has not yet, returning null is correct. Null means the
        // caller falls back to UTC, which is a zone we are NOT asserting is theirs.
        var kioskZone = await displayRepo.GetAsync(userId, ct);
        if (!string.IsNullOrWhiteSpace(kioskZone?.IanaTimeZone)
            && kioskZone.IsTimeZoneAutoDetected
            && IsValidZone(kioskZone.IanaTimeZone))
        {
            return kioskZone.IanaTimeZone;
        }

        return null;
    }

    /// <summary>
    /// FHQ-178: the kiosk reports the zone its own operating system is set to, read in the browser
    /// from <c>Intl.DateTimeFormat().resolvedOptions().timeZone</c>. The kiosk physically sits in the
    /// family's home, so this is the one automatic source that actually describes them — no network
    /// call, no third party, and nothing about their location leaves the house.
    /// <para>
    /// Reported on every kiosk load rather than stored once, which is what makes a change to the
    /// kiosk's OS timezone propagate: there is nothing to poll and no extra state to keep. An
    /// EXPLICIT zone is never touched — if the family chose one, the kiosk's OS is not evidence
    /// against it, and there is nothing to detect until they reset to auto.
    /// </para>
    /// </summary>
    public async Task SetKioskZoneAsync(string? ianaZone, CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId) || string.IsNullOrWhiteSpace(ianaZone) || !IsValidZone(ianaZone))
            return;

        var display = await displayRepo.GetAsync(userId, ct);

        // Explicit wins, always. Only an auto-detected zone (or none at all) follows the kiosk.
        if (display is not null
            && !string.IsNullOrWhiteSpace(display.IanaTimeZone)
            && !display.IsTimeZoneAutoDetected)
        {
            return;
        }

        // A SAVED LOCATION also wins. The precedence here must match ResolveAutoZoneAsync's
        // (location, then kiosk) or the two fight: this runs on every kiosk load, so a kiosk in one
        // zone would silently overwrite the zone derived from a location the family deliberately
        // chose in another — "New York" saved, then re-themed to the kiosk's own zone on the next
        // page load, with the difference visible only on their Google calendar.
        var location = await locationRepo.GetAsync(userId, ct);
        if (location is not null)
        {
            var derived = timeZoneLookup.GetTimeZone(location.Latitude, location.Longitude);
            if (!string.IsNullOrWhiteSpace(derived) && IsValidZone(derived)) return;
        }

        // No write when it already matches: this runs on every kiosk load, and an unconditional
        // upsert would touch UpdatedAt on each one.
        if (string.Equals(display?.IanaTimeZone, ianaZone, StringComparison.Ordinal)
            && display?.IsTimeZoneAutoDetected == true)
        {
            return;
        }

        await PersistZoneAsync(userId, display, ianaZone, isAutoDetected: true, ct);
    }

    public async Task<string?> GetSendZoneAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;

        // READ-ONLY. The effective zone is persisted ONCE at a change point: location auto-discovery
        // (the kiosk reporting its OS zone -> SetKioskZoneAsync), manual set, or location
        // save/reset. The outbound Google-write path must NEVER resolve here — doing so would call
        // ip-api on every write AND write the request-scoped DbContext mid event/member operation
        // (which shifted sync timing and re-exposed the membership flap). Unset -> null (caller -> UTC).
        var display = await displayRepo.GetAsync(userId, ct);
        return string.IsNullOrWhiteSpace(display?.IanaTimeZone) ? null : display.IanaTimeZone;
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
