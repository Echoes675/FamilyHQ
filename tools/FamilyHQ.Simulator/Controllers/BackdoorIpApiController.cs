namespace FamilyHQ.Simulator.Controllers;

using FamilyHQ.Simulator.Data;
using FamilyHQ.Simulator.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// FHQ-43: lets an E2E scenario drive the ip-api auto-detect mock (<see cref="IpApiController"/>)
/// deterministically. POST sets the auto-detect response (at minimum the IANA timezone); DELETE
/// clears it so the controller reverts to the Edinburgh / Europe/London default. Persisted via
/// <see cref="SimContext"/> as a single row, mirroring the SimulatedLocation backdoor.
/// </summary>
[ApiController]
[Route("api/simulator/backdoor/ipapi")]
public class BackdoorIpApiController(SimContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SetResponse([FromBody] SetIpApiResponseRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Timezone))
            return BadRequest("timezone is required.");

        // Single-row table: replace any existing override so the auto-detect result is unambiguous.
        var existing = await db.IpApiResponses.ToListAsync(ct);
        db.IpApiResponses.RemoveRange(existing);

        var fallback = new SimulatedIpApiResponse();
        db.IpApiResponses.Add(new SimulatedIpApiResponse
        {
            Id = 1,
            City = string.IsNullOrWhiteSpace(request.City) ? fallback.City : request.City,
            RegionName = string.IsNullOrWhiteSpace(request.RegionName) ? fallback.RegionName : request.RegionName,
            Latitude = request.Lat ?? fallback.Latitude,
            Longitude = request.Lon ?? fallback.Longitude,
            Timezone = request.Timezone
        });

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "ip-api auto-detect response set" });
    }

    [HttpDelete]
    public async Task<IActionResult> ClearResponse(CancellationToken ct)
    {
        var existing = await db.IpApiResponses.ToListAsync(ct);
        db.IpApiResponses.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "ip-api auto-detect response cleared" });
    }
}

public record SetIpApiResponseRequest(string Timezone, string? City, string? RegionName, double? Lat, double? Lon);
