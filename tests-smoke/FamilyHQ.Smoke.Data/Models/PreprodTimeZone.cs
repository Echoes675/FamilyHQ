namespace FamilyHQ.Smoke.Data.Models;

/// <summary>
/// Preprod's family time zone, as <c>GET /api/settings/timezone</c> describes it.
/// <see cref="EffectiveIanaZone"/> is the one that matters: it is the zone FamilyHQ actually applies to
/// an outbound write, whether it came from an explicit setting or from the kiosk reporting its own.
/// </summary>
public sealed record PreprodTimeZone(string EffectiveIanaZone, bool IsExplicit, string? ExplicitIanaZone);
