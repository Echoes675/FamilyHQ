namespace FamilyHQ.Simulator.Models;

/// <summary>
/// FHQ-43: the per-scenario override for the ip-api auto-detect mock (<c>IpApiController</c>).
/// Dev/staging geolocation auto-detect calls the Simulator instead of real ip-api; the WebApi reads
/// the <c>timezone</c> field to resolve a user's IANA zone when no zone is explicitly configured.
/// A single row (id fixed to 1) holds the currently-configured auto-detect response so E2E can drive
/// the auto-detect outcome deterministically. When no row exists the controller falls back to the
/// Edinburgh / Europe/London default.
/// </summary>
public class SimulatedIpApiResponse
{
    // Single-row table: always id 1 (the mock has one global auto-detect result at a time).
    public int Id { get; set; } = 1;
    public string City { get; set; } = "Edinburgh";
    public string RegionName { get; set; } = "Scotland";
    public double Latitude { get; set; } = 55.9533;
    public double Longitude { get; set; } = -3.1883;
    public string Timezone { get; set; } = "Europe/London";
}
