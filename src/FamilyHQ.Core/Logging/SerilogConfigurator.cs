using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace FamilyHQ.Core.Logging;

/// <summary>
/// Shared Serilog wiring used by every FamilyHQ host (WebApi, Simulator).
/// <para>
/// Verbosity is governed by the existing <c>Logging:LogLevel</c> configuration section
/// (no separate Serilog level section): <c>Logging:LogLevel:Default</c> sets the minimum
/// level and every other <c>Logging:LogLevel:&lt;Category&gt;</c> becomes a per-source override.
/// </para>
/// <para>
/// A Console sink is always attached (preserving Docker stdout). A Seq sink is only attached
/// when <c>Seq:ServerUrl</c> is configured, making the call a prod-safe no-op otherwise.
/// </para>
/// </summary>
public static class SerilogConfigurator
{
    private const LogEventLevel DefaultMinimumLevel = LogEventLevel.Information;
    private const string DefaultLevelKey = "Default";

    /// <summary>
    /// Applies the shared FamilyHQ logging configuration (levels, enrichers, sinks) to the
    /// supplied <see cref="LoggerConfiguration"/>.
    /// </summary>
    /// <param name="loggerConfiguration">The Serilog logger configuration to mutate.</param>
    /// <param name="configuration">Application configuration (read for levels + Seq settings).</param>
    /// <param name="application">Value for the <c>Application</c> enriched property.</param>
    /// <param name="environment">Value for the <c>Environment</c> enriched property.</param>
    public static void Configure(
        LoggerConfiguration loggerConfiguration,
        IConfiguration configuration,
        string application,
        string environment)
    {
        ArgumentNullException.ThrowIfNull(loggerConfiguration);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        var logLevelSection = configuration.GetSection("Logging:LogLevel");

        var minimumLevel = ResolveLevel(logLevelSection[DefaultLevelKey]) ?? DefaultMinimumLevel;
        loggerConfiguration.MinimumLevel.Is(minimumLevel);

        foreach (var child in logLevelSection.GetChildren())
        {
            if (string.Equals(child.Key, DefaultLevelKey, StringComparison.Ordinal))
            {
                continue;
            }

            var overrideLevel = ResolveLevel(child.Value);
            if (overrideLevel is not null)
            {
                loggerConfiguration.MinimumLevel.Override(child.Key, overrideLevel.Value);
            }
        }

        loggerConfiguration
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProperty("Application", application)
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.Console();

        var seqServerUrl = configuration["Seq:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(seqServerUrl))
        {
            var seqApiKey = configuration["Seq:ApiKey"];
            loggerConfiguration.WriteTo.Seq(
                seqServerUrl,
                apiKey: string.IsNullOrWhiteSpace(seqApiKey) ? null : seqApiKey);
        }
    }

    /// <summary>
    /// Maps a Microsoft <see cref="LogLevel"/> configuration string to a Serilog
    /// <see cref="LogEventLevel"/>. <c>None</c> suppresses logging (mapped to a level above Fatal).
    /// Returns <c>null</c> when the value is empty or unrecognised so the caller can fall back.
    /// </summary>
    private static LogEventLevel? ResolveLevel(string? configuredLevel)
    {
        if (string.IsNullOrWhiteSpace(configuredLevel))
        {
            return null;
        }

        if (!Enum.TryParse<LogLevel>(configuredLevel, ignoreCase: true, out var logLevel))
        {
            return null;
        }

        return logLevel switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            // None: suppress all events by setting the floor above the highest level.
            LogLevel.None => (LogEventLevel)((int)LogEventLevel.Fatal + 1),
            _ => null
        };
    }
}
