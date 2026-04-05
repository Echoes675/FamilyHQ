namespace FamilyHQ.Simulator.Controllers;

using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("json")]
public class IpApiController : ControllerBase
{
    // Simulates ip-api.com auto-detection: always returns the dev server's fixed location.
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "success",
        city = "Edinburgh",
        regionName = "Scotland",
        country = "",
        lat = 55.9533,
        lon = -3.1883
    });
}
