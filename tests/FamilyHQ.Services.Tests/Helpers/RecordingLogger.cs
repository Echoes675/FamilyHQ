using Microsoft.Extensions.Logging;

namespace FamilyHQ.Services.Tests.Helpers;

/// <summary>
/// Simple test logger that captures log records so assertions can inspect them.
/// Avoids the need for a third-party FakeLogger package while providing the
/// same verification surface.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _records = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Records => _records;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _records.Add((logLevel, formatter(state, exception)));
    }
}
