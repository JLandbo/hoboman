using Microsoft.Extensions.Logging;

namespace Hoboman.Tests;

public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, int Thread, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, Environment.CurrentManagedThreadId, formatter(state, exception)));
}
