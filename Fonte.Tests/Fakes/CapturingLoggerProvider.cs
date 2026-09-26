using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Fonte.Tests.Fakes;

public sealed record CapturedLog(
    string Category,
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> State,
    Exception? Exception);

/// <summary>Guarda as entradas de log para que os testes possam conferi-las.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new CapturedLog(
                category,
                logLevel,
                formatter(state, exception),
                values.ToDictionary(v => v.Key, v => v.Value),
                exception));
        }
    }
}
