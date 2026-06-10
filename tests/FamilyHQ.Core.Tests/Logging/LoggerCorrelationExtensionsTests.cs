using FamilyHQ.Core.Constants;
using FamilyHQ.Core.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace FamilyHQ.Core.Tests.Logging;

// FHQ-65: background tasks open a fresh-GUID CorrelationId logging scope per invocation.
// BeginCorrelationScope is the single place that id is minted, so these tests lock its
// contract: the exact scope-property key, that the value is a GUID, and that consecutive
// invocations get DISTINCT ids (per-invocation freshness, not per-lifetime).
public class LoggerCorrelationExtensionsTests
{
    private sealed class ScopeCapturingLogger : ILogger
    {
        public List<object?> Scopes { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return new Noop();
        }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void BeginCorrelationScope_OpensScopeWithCorrelationIdGuid()
    {
        var logger = new ScopeCapturingLogger();

        using (logger.BeginCorrelationScope()) { }

        var state = logger.Scopes.Should().ContainSingle().Subject as IReadOnlyDictionary<string, object>;
        state.Should().NotBeNull();
        state!.Should().ContainKey(CorrelationConstants.CorrelationIdLogProperty);
        Guid.TryParse(state[CorrelationConstants.CorrelationIdLogProperty].ToString(), out _)
            .Should().BeTrue("the correlation id should be a GUID string");
    }

    [Fact]
    public void BeginCorrelationScope_ProducesDistinctIdsPerInvocation()
    {
        var logger = new ScopeCapturingLogger();

        using (logger.BeginCorrelationScope()) { }
        using (logger.BeginCorrelationScope()) { }

        var first = ((IReadOnlyDictionary<string, object>)logger.Scopes[0]!)[CorrelationConstants.CorrelationIdLogProperty];
        var second = ((IReadOnlyDictionary<string, object>)logger.Scopes[1]!)[CorrelationConstants.CorrelationIdLogProperty];
        second.Should().NotBe(first);
    }
}
