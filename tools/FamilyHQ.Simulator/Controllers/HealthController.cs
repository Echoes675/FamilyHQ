using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.Simulator.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Get() => Ok(new
    {
        status = "healthy",
        service = "simulator",
        timestamp = DateTimeOffset.UtcNow
    });
}
