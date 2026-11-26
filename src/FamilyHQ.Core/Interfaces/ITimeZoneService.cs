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
    /// <see cref="SetKioskZoneAsync"/>, manual set, or location save/reset). Returns null
    /// when no zone is persisted (caller -> UTC). This is what the outbound Google-write path calls,
    /// so it must not touch ip-api or the request DbContext.
    /// </summary>
    Task<string?> GetSendZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// The zone every DISPLAY path reads a day or a time of day in: <see cref="GetSendZoneAsync"/>
    /// resolved to a <see cref="TimeZoneInfo"/>, falling back to UTC when nothing is stored or when
    /// the stored id is one this host's tzdb cannot read.
    /// </summary>
    /// <remarks>
    /// One resolver rather than a guard per caller. The reminders endpoint and the month endpoint both
    /// need this exact answer, and a private helper copied into the second of them is how a rule
    /// starts having two implementations that agree until they do not.
    /// <para>
    /// Never throws, because no display surface is worth failing over an unreadable stored id: UTC is
    /// a zone we are NOT asserting is theirs, which is also what a null from
    /// <see cref="GetSendZoneAsync"/> has always meant.
    /// </para>
    /// </remarks>
    Task<TimeZoneInfo> GetDisplayZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>, guarded: null for a null or blank id, and
    /// null plus one warning for a non-blank id this host cannot resolve.
    /// </summary>
    /// <remarks>
    /// Public because a second id needs the same treatment and is not the household's: an event's
    /// OWNING CALENDAR carries a zone of its own, Google-supplied, which the reminders endpoint
    /// resolves to anchor a ping calculation. Distinct from <see cref="IsValidZone"/>, which asks
    /// NodaTime's tzdb whether an id is a known IANA name — a different question from whether this
    /// host can build a <see cref="TimeZoneInfo"/> for it.
    /// </remarks>
    TimeZoneInfo? TryResolveZone(string? ianaId);

    /// <summary>
    /// Change point: persist an already-resolved auto-detected zone (e.g. from ip-api auto-discovery)
    /// ONCE, only if the user has no zone yet. No-op if a zone (auto or explicit) is already set, if
    /// the zone is null/blank, or if it is not a valid IANA id. Does not call ip-api.
    /// </summary>

    /// <summary>
    /// FHQ-178: record the zone the kiosk's own OS is set to. Ignored when the family has chosen an
    /// explicit zone. Reported on every kiosk load, so a change to the kiosk's timezone propagates
    /// without polling.
    /// </summary>
    Task SetKioskZoneAsync(string? ianaZone, CancellationToken ct = default);

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
