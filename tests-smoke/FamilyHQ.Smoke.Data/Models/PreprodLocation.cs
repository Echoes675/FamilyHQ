namespace FamilyHQ.Smoke.Data.Models;

/// <summary>Preprod's saved location, as <c>GET /api/settings/location</c> describes it.</summary>
public sealed record PreprodLocation(string PlaceName, bool IsAutoDetected);
