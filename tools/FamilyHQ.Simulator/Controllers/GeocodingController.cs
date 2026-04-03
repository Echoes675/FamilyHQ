namespace FamilyHQ.Simulator.Controllers;

using System.Globalization;
using FamilyHQ.Simulator.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("search")]
public class GeocodingController(SimContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        var location = await db.SimulatedLocations
            .Where(l => EF.Functions.ILike(l.PlaceName, $"%{q}%"))
            .FirstOrDefaultAsync(ct);

        if (location is null)
            return Ok(Array.Empty<object>());

        var result = new[]
        {
            new
            {
                lat = location.Latitude.ToString("F7", CultureInfo.InvariantCulture),
                lon = location.Longitude.ToString("F7", CultureInfo.InvariantCulture),
                display_name = location.PlaceName
            }
        };

        return Ok(result);
    }
}
