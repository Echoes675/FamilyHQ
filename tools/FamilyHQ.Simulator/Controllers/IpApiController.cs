namespace FamilyHQ.Simulator.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("json")]
public class IpApiController : ControllerBase
{
    // Simulates ip-api.com auto-detection. Dev/staging geolocation auto-detect hits this instead of
    // real ip-api; the WebApi reads the `timezone` field to resolve a user's IANA zone when none is
    // configured (FHQ-43). Always returns the fixed Edinburgh / Europe/London constant — no shared
    // mutable state so parallel E2E scenarios cannot race on this response.
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "success",
            city = "Edinburgh",
            regionName = "Scotland",
            country = "",
            lat = 55.9533,
            lon = -3.1883,
            timezone = "Europe/London"
        });
    }
}
