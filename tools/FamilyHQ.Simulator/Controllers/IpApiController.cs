namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("json")]
public class IpApiController : ControllerBase
{
    private readonly SimContext _db;

    public IpApiController(SimContext db)
    {
        _db = db;
    }

    // Simulates ip-api.com auto-detection. Dev/staging geolocation auto-detect hits this instead of
    // real ip-api; the WebApi reads the `timezone` field to resolve a user's IANA zone when none is
    // configured (FHQ-43). Returns the per-scenario override when set (BackdoorIpApiController), else
    // the fixed Edinburgh / Europe/London default.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var configured = await _db.IpApiResponses.AsNoTracking().FirstOrDefaultAsync(ct);
        var result = configured ?? new SimulatedIpApiResponse();

        return Ok(new
        {
            status = "success",
            city = result.City,
            regionName = result.RegionName,
            country = "",
            lat = result.Latitude,
            lon = result.Longitude,
            timezone = result.Timezone
        });
    }
}
