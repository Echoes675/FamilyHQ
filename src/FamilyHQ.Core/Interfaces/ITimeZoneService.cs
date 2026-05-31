namespace FamilyHQ.Core.Interfaces;

public interface ITimeZoneService
{
    /// <summary>
    /// Resolves the current user's effective IANA zone: explicit setting -> derived from
    /// current location (GeoTimeZone) -> ip-api auto-detect -> null (caller falls back to UTC).
    /// </summary>
    Task<string?> GetEffectiveIanaZoneAsync(CancellationToken ct = default);

    /// <summary>UTC instant -> "uuuu-MM-ddTHH:mm:ss" wall-clock in the given IANA zone (NodaTime).</summary>
    /// <remarks>Pass a zone validated via <see cref="IsValidZone"/>; throws ArgumentException otherwise.</remarks>
    string ToZonedWallClock(DateTimeOffset utcInstant, string ianaZone);

    bool IsValidZone(string ianaZone);
    IReadOnlyList<string> GetAvailableZoneIds();
}
