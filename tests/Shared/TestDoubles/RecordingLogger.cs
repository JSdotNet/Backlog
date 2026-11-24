using Microsoft.Extensions.Logging;

namespace Backlog.Tests;

/// <summary>One entry a <see cref="RecordingLogger{T}"/> saw: its level, its rendered
/// message, its structured fields by name, and the exception it carried.</summary>
internal sealed record LoggedEntry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Fields, Exception? Exception);

/// <summary>A logger that keeps what it is told, so a test can say what a
/// failure left in the structured logs, and what never reached them.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LoggedEntry> Entries { get; } = [];

    /// <summary>The level of every entry, in order.</summary>
    public IReadOnlyList<LogLevel> Levels => [.. Entries.Select(entry => entry.Level)];

    /// <summary>The rendered message of every warning, in order.</summary>
    public IReadOnlyList<string> Warnings =>
        [.. Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message)];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        Entries.Add(new LoggedEntry(logLevel, formatter(state, exception), fields, exception));
    }
}
