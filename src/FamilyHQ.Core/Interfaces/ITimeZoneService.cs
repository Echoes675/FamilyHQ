namespace FamilyHQ.Core.Interfaces;

public interface ITimeZoneService
{
    /// <summary>
    /// Explicit per-user setting -> derived from saved location (GeoTimeZone) -> null.
    /// NO network/ip-api call. Safe to call from the settings display path.
    /// </summary>
    Task<string?> GetConfiguredIanaZoneAsync(CancellationToken ct = default);

    /// <summary>
    /// Configured zone (explicit or location-derived), else ip-api auto-detect (cached), else null.
    /// Used for the OUTBOUND Google Calendar payload only.
    /// </summary>
    Task<string?> GetEffectiveIanaZoneAsync(CancellationToken ct = default);

    /// <summary>UTC instant -> "uuuu-MM-ddTHH:mm:ss" wall-clock in the given IANA zone (NodaTime).</summary>
    /// <remarks>Pass a zone validated via <see cref="IsValidZone"/>; throws ArgumentException otherwise.</remarks>
    string ToZonedWallClock(DateTimeOffset utcInstant, string ianaZone);

    bool IsValidZone(string ianaZone);
    IReadOnlyList<string> GetAvailableZoneIds();
}
