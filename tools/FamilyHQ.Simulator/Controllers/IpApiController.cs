namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("json")]
public class IpApiController(SimContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var location = await db.SimulatedLocations
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync(ct);

        if (location is null)
            return Ok(new { status = "fail", message = "No locations seeded" });

        var parts = location.PlaceName.Split(", ");
        var city = parts.ElementAtOrDefault(0) ?? "";
        var regionName = parts.ElementAtOrDefault(1) ?? "";
        var country = parts.ElementAtOrDefault(2) ?? "";

        return Ok(new
        {
            status = "success",
            city,
            regionName,
            country,
            lat = location.Latitude,
            lon = location.Longitude
        });
    }
}
