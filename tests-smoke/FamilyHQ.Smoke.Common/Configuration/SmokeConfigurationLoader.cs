using Microsoft.Extensions.Configuration;

namespace FamilyHQ.Smoke.Common.Configuration;

/// <summary>
/// Loads <see cref="SmokeConfiguration"/> from <c>appsettings.json</c> plus environment overrides —
/// the same pattern as the E2E suite's <c>ConfigurationLoader</c>, so CI wiring reads the same way in
/// both suites (<c>Smoke__BaseUrl=…</c> alongside <c>TestConfiguration__BaseUrl=…</c>).
/// <para>
/// The result is cached for the process: the settings cannot change mid-run, and re-reading the file
/// per call would put a file read behind every HTTP client construction.
/// </para>
/// </summary>
public static class SmokeConfigurationLoader
{
    private static readonly Lazy<SmokeConfiguration> Cached = new(Build);

    public static SmokeConfiguration Load() => Cached.Value;

    private static SmokeConfiguration Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var smokeConfiguration = new SmokeConfiguration();
        configuration.GetSection(SmokeConfiguration.SectionName).Bind(smokeConfiguration);
        return smokeConfiguration;
    }
}
