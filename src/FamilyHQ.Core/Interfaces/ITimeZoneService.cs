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
    /// Returns the persisted effective zone for the current user as a pure DB read — never resolves
    /// or persists. The zone is set ONCE at a change point (auto-discovery via
    /// <see cref="EnsureAutoZonePersistedAsync"/>, manual set, or location save/reset). Returns null
    /// when no zone is persisted (caller -> UTC). This is what the outbound Google-write path calls,
    /// so it must not touch ip-api or the request DbContext.
    /// </summary>
    Task<string?> GetSendZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// Change point: persist an already-resolved auto-detected zone (e.g. from ip-api auto-discovery)
    /// ONCE, only if the user has no zone yet. No-op if a zone (auto or explicit) is already set, if
    /// the zone is null/blank, or if it is not a valid IANA id. Does not call ip-api.
    /// </summary>
    Task EnsureAutoZonePersistedAsync(string? ianaZone, CancellationToken ct = default);

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
