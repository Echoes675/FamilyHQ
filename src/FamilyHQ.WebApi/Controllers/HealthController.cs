using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

// Explicitly public (FHQ-98): deployment health checks and E2E probes call /api/health
// without credentials; the payload contains no user data.
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private static readonly string Version = ResolveVersion();

    [HttpGet]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

        return Ok(new
        {
            status = "healthy",
            service = "webapi",
            version = Version,
            timestamp = DateTimeOffset.UtcNow,
        });
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(HealthController).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "0.0.0-unknown" : assemblyVersion;
    }
}
