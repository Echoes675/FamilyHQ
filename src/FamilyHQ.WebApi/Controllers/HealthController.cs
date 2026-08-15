using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace FamilyHQ.WebApi.Controllers;

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
            return StripBuildMetadata(informational);
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "0.0.0-unknown" : assemblyVersion;
    }

    /// <summary>
    /// FHQ-103: this endpoint is anonymous, so it must not hand an unauthenticated caller the
    /// "+&lt;gitsha&gt;" build metadata MinVer appends to AssemblyInformationalVersion — that
    /// fingerprints the exact deployed commit.
    /// <para>The pre-release label is deliberately KEPT. The WASM client compares this value against
    /// its own baked-in version to decide whether to show the update banner and reload
    /// (<c>VersionService.VersionsMatch</c>), and it strips build metadata only. Dropping the
    /// pre-release here as well would make every untagged build (e.g. <c>1.1.0-alpha.0.5</c>)
    /// compare unequal forever — a reload loop on every kiosk.</para>
    /// </summary>
    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
