using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "healthy",
        service = "webapi",
        timestamp = DateTimeOffset.UtcNow
    });
}
