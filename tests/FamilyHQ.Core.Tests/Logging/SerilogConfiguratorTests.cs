using System.Collections.Concurrent;
using FamilyHQ.Core.Logging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace FamilyHQ.Core.Tests.Logging;

public class SerilogConfiguratorTests
{
    private const string Application = "FamilyHQ.Tests";
    private const string Environment = "UnitTest";

    [Fact]
    public void Configure_EnrichesEventsWithApplicationEnvironmentAndMachineName()
    {
        var sink = new InMemorySink();
        using var logger = BuildLogger(sink, configuration: BuildConfiguration());

        logger.Information("hello");

        var logEvent = sink.Events.Should().ContainSingle().Subject;
        ScalarValue(logEvent, "Application").Should().Be(Application);
        ScalarValue(logEvent, "Environment").Should().Be(Environment);
        ScalarValue(logEvent, "MachineName").Should().Be(System.Environment.MachineName);
    }

    [Fact]
    public void Configure_CapturesLogContextProperties_ProvingCorrelationEnrichment()
    {
        var sink = new InMemorySink();
        using var logger = BuildLogger(sink, configuration: BuildConfiguration());

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", "abc"))
        {
            logger.Information("correlated");
        }

        var logEvent = sink.Events.Should().ContainSingle().Subject;
        ScalarValue(logEvent, "CorrelationId").Should().Be("abc");
    }

    [Fact]
    public void Configure_WithNoSeqServerUrl_BuildsAndLogsWithoutThrowing()
    {
        var sink = new InMemorySink();

        // No Seq:ServerUrl is supplied (prod-safe no-op): building + logging must not throw.
        var act = () =>
        {
            using var logger = BuildLogger(sink, configuration: BuildConfiguration());
            logger.Information("prod-safe");
        };

        act.Should().NotThrow();
        sink.Events.Should().ContainSingle();
    }

    [Fact]
    public void Configure_HonoursDefaultMinimumLevelFromLoggingSection()
    {
        var sink = new InMemorySink();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning"
        });
        using var logger = BuildLogger(sink, configuration);

        logger.Information("dropped");
        logger.Warning("kept");

        sink.Events.Should().ContainSingle()
            .Which.Level.Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void Configure_AppliesPerCategoryOverrideFromLoggingSection()
    {
        var sink = new InMemorySink();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Noisy.Component"] = "Error"
        });
        using var logger = BuildLogger(sink, configuration);

        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Noisy.Component")
            .Information("dropped-by-override");
        logger.ForContext(Serilog.Core.Constants.SourceContextPropertyName, "Quiet.Component")
            .Information("kept");

        var logEvent = sink.Events.Should().ContainSingle().Subject;
        ScalarValue(logEvent, Serilog.Core.Constants.SourceContextPropertyName).Should().Be("Quiet.Component");
    }

    private static Logger BuildLogger(InMemorySink sink, IConfiguration configuration)
    {
        var loggerConfiguration = new LoggerConfiguration();
        SerilogConfigurator.Configure(loggerConfiguration, configuration, Application, Environment);
        loggerConfiguration.WriteTo.Sink(sink);
        return loggerConfiguration.CreateLogger();
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? new Dictionary<string, string?>())
            .Build();
    }

    private static object? ScalarValue(LogEvent logEvent, string propertyName)
    {
        logEvent.Properties.Should().ContainKey(propertyName);
        return ((ScalarValue)logEvent.Properties[propertyName]).Value;
    }

    private sealed class InMemorySink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
