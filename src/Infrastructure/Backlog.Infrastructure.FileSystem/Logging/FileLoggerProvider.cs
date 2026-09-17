using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.FileSystem.Logging;

/// <summary>
/// One plain-text log file per day, in a folder of the host's choosing.
/// <para>
/// This exists because an installed build had nowhere to put an exception. The
/// sync workers catch everything a cycle throws and put one sentence on screen
/// — "could not be reached, try again in a moment" — on the promise that the
/// detail went to the log. Under a debugger it did; on a second PC with no
/// debugger it went to a <c>Debug</c> provider that was compiled out, and the
/// person was left with the sentence. A file is the sink that is there whether
/// or not anybody is watching.
/// </para>
/// <para>
/// Deliberately small: no rolling by size, no background writer, no structured
/// output. A line is appended under a lock with the file opened and closed per
/// write, which is what makes the file readable while the app is running and
/// keeps a crash from losing a buffered tail. That costs a file open per line,
/// and the filters <see cref="FileLoggingRegistration.AddFileLogging"/> puts on
/// the provider keep the line rate to failures and the app's own progress, so
/// the cost is nothing anyone measures.
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Files start with this and end with the day, so pruning can tell
    /// its own files from anything else in the folder by name alone.</summary>
    public const string FilePrefix = "backlog-";

    /// <summary>Two weeks. Long enough to come back to a failure after a
    /// holiday, short enough that the folder never becomes something to clean.</summary>
    internal static readonly TimeSpan RetainFor = TimeSpan.FromDays(14);

    private readonly string _directory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(string directory)
        : this(directory, TimeProvider.System)
    {
    }

    public FileLoggerProvider(string directory, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(time);

        _directory = directory;
        _time = time;

        try
        {
            Directory.CreateDirectory(directory);
            Prune();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be made is a log that cannot be written, and
            // every write below already tolerates that. Refusing to construct
            // would take the app down for the sake of its own diagnostics.
        }
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static (name, owner) => new FileLogger(name, owner), this);

    public void Dispose()
    {
        // Nothing is held open between writes, so there is nothing to flush.
    }

    /// <summary>The file for a given moment: today's, by the local date, because
    /// the person reading it will be matching it against the clock on their
    /// own screen.</summary>
    internal string PathFor(DateTimeOffset localNow) =>
        Path.Combine(_directory, $"{FilePrefix}{localNow:yyyyMMdd}.log");

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = _time.GetLocalNow();

        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(Abbreviate(level)).Append("] ")
            .Append(category).Append(": ")
            .Append(message)
            .AppendLine();

        if (exception is not null)
        {
            // Every line of the exception indented, so a reader scanning the
            // left margin for timestamps does not trip over a stack frame.
            foreach (var part in exception.ToString().Split('\n'))
            {
                line.Append("    ").Append(part.TrimEnd('\r')).AppendLine();
            }
        }

        lock (_gate)
        {
            try
            {
                File.AppendAllText(PathFor(now), line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written is not a reason to fail the
                // thing that was being logged.
            }
        }
    }

    private void Prune()
    {
        var cutoff = _time.GetLocalNow().Date - RetainFor;

        foreach (var file in Directory.EnumerateFiles(_directory, FilePrefix + "*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)[FilePrefix.Length..];

            if (DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < cutoff)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Somebody has it open. It goes next time.
                }
            }
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???"
    };

    /// <summary>One per category, holding nothing but its name: the provider
    /// owns the file, the clock and the lock.</summary>
    private sealed class FileLogger(string category, FileLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!IsEnabled(logLevel)) return;

            owner.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
