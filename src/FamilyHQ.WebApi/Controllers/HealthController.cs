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
        // Strip before the emptiness check: a pathological metadata-only value would otherwise
        // publish an empty version instead of falling through to the assembly version.
        var publicVersion = StripBuildMetadata(informational ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(publicVersion))
        {
            return publicVersion;
        }

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "0.0.0-unknown" : assemblyVersion;
    }

    /// <summary>
    /// FHQ-103: this endpoint is anonymous, so it publishes the SemVer core only — never the
    /// "+&lt;commit sha&gt;" build metadata the .NET SDK appends to AssemblyInformationalVersion from
    /// SourceLink's SourceRevisionId (the full 40-char SHA), which would fingerprint the exact
    /// build.
    /// <para>Defence-in-depth rather than a live exposure: deployed images build with no <c>.git</c>
    /// directory (excluded by <c>.dockerignore</c>), so today they emit no metadata to strip —
    /// verified against the dev host, which reports a bare <c>1.1.0-alpha.0</c>. This keeps that
    /// true if the build ever gains git context.</para>
    /// <para>The pre-release label is deliberately KEPT. The WASM client compares this value against
    /// its own baked-in version to decide whether to show the update banner and reload
    /// (<c>VersionService.VersionsMatch</c>), and it strips build metadata only. Dropping the
    /// pre-release here as well would make every pre-release build compare unequal forever, so
    /// each SignalR reconnect would re-trigger the banner and <c>location.reload()</c> — and the
    /// reload resets the guard that bounds it.</para>
    /// </summary>
    internal static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
