namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/simulator/backdoor/location")]
public class BackdoorLocationController(SimContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SetLocation([FromBody] SetLocationRequest request, CancellationToken ct)
    {
        var existing = await db.SimulatedLocations
            .Where(l => l.PlaceName == request.PlaceName)
            .ToListAsync(ct);
        db.SimulatedLocations.RemoveRange(existing);

        db.SimulatedLocations.Add(new SimulatedLocation
        {
            PlaceName = request.PlaceName,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Location data set" });
    }

    [HttpDelete]
    public async Task<IActionResult> ClearLocation([FromQuery] string placeName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(placeName))
            return BadRequest("placeName is required.");

        var existing = await db.SimulatedLocations
            .Where(l => l.PlaceName == placeName)
            .ToListAsync(ct);
        db.SimulatedLocations.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Location data cleared" });
    }
}

public record SetLocationRequest(string PlaceName, double Latitude, double Longitude);
