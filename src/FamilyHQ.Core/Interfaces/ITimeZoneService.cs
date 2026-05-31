namespace FamilyHQ.Core.Interfaces;

public interface ITimeZoneService
{
    /// <summary>
    /// Resolves the AUTO zone from current state, ignoring any explicit setting:
    /// saved location -> GeoTimeZone(lat,lon); else ip-api (LocationService); else null.
    /// Calls ip-api/GeoTimeZone — only invoke at change points, never on the outbound path.
    /// </summary>
    Task<string?> ResolveAutoZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the persisted effective zone for the current user. If unset, lazily resolves the
    /// AUTO zone ONCE, persists it (IanaTimeZone + IsTimeZoneAutoDetected=true), and returns it.
    /// If resolution yields null, returns null (caller -> UTC). This is what the outbound path
    /// calls; after first use it is a pure DB read.
    /// </summary>
    Task<string?> GetSendZoneAsync(CancellationToken ct = default);

    /// <summary>Change point: persist an explicit, sticky zone (IsTimeZoneAutoDetected=false).</summary>
    Task SetExplicitZoneAsync(string ianaZone, CancellationToken ct = default);

    /// <summary>Change point: re-resolve the AUTO zone, persist it (IsTimeZoneAutoDetected=true).</summary>
    Task ResetToAutoZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// Change point: if the zone is auto-detected (or unset), re-resolve the AUTO zone and persist
    /// it; if explicit, no-op (the explicit zone is sticky across location changes).
    /// </summary>
    Task RepersistAutoIfNotExplicitAsync(CancellationToken ct = default);

    /// <summary>UTC instant -> "uuuu-MM-ddTHH:mm:ss" wall-clock in the given IANA zone (NodaTime).</summary>
    /// <remarks>Pass a zone validated via <see cref="IsValidZone"/>; throws ArgumentException otherwise.</remarks>
    string ToZonedWallClock(DateTimeOffset utcInstant, string ianaZone);

    bool IsValidZone(string ianaZone);
    IReadOnlyList<string> GetAvailableZoneIds();
}
