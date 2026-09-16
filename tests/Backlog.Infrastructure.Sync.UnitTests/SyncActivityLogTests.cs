using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The log the footer reads: what it keeps, in what order, how much of it, and
/// that every entry also leaves through the logger — which is the half a person
/// reads from the Aspire dashboard rather than from the window.
/// </summary>
public sealed class SyncActivityLogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Entries_are_read_back_newest_first()
    {
        var clock = new FakeTimeProvider(Noon);
        var log = new SyncActivityLog(time: clock);

        log.Record(SyncDirection.Sent, SyncItemKind.Task, "a", "First");
        clock.Advance(TimeSpan.FromSeconds(1));
        log.Record(SyncDirection.Received, SyncItemKind.Task, "b", "Second");

        var entries = log.Snapshot();

        Assert.Equal(["Second", "First"], entries.Select(entry => entry.Title).ToArray());
        Assert.Equal(Noon.AddSeconds(1), entries[0].At);
        Assert.Equal(SyncDirection.Received, entries[0].Direction);
    }

    /// <summary>A republish of a whole backlog must not turn the footer's window
    /// into the backlog. The oldest entries go, the newest stay.</summary>
    [Fact]
    public void The_log_keeps_the_newest_entries_and_lets_the_oldest_go()
    {
        var log = new SyncActivityLog();

        for (var i = 0; i < SyncActivityLog.Capacity + 5; i++)
        {
            log.Record(SyncDirection.Sent, SyncItemKind.Task, i.ToString(), $"Task {i}");
        }

        var entries = log.Snapshot();

        Assert.Equal(SyncActivityLog.Capacity, entries.Count);
        Assert.Equal(SyncActivityLog.Capacity, log.Count);
        Assert.Equal($"Task {SyncActivityLog.Capacity + 4}", entries[0].Title);
        Assert.Equal("Task 5", entries[^1].Title);
    }

    [Fact]
    public void Recording_raises_Changed_so_a_screen_can_redraw()
    {
        var log = new SyncActivityLog();
        var raised = 0;
        log.Changed += () => raised++;

        log.Record(SyncDirection.Sent, SyncItemKind.Session, "s", "A session");

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// The structured line, with direction and kind as named properties rather
    /// than folded into the sentence: that is what lets the dashboard filter on
    /// "everything received" without a regular expression over the text.
    /// </summary>
    [Fact]
    public void Every_entry_is_written_to_the_logger_with_its_direction_and_kind_as_properties()
    {
        var logger = new CapturingLogger();
        var log = new SyncActivityLog(logger);

        log.Record(SyncDirection.Received, SyncItemKind.Capture, "c-1", "Buy milk", "withdrawn");

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("Received", line.Message, StringComparison.Ordinal);
        Assert.Contains("Capture", line.Message, StringComparison.Ordinal);
        Assert.Contains("\"Buy milk\"", line.Message, StringComparison.Ordinal);
        Assert.Contains("(withdrawn)", line.Message, StringComparison.Ordinal);
        Assert.Equal(SyncDirection.Received, line.Properties["Direction"]);
        Assert.Equal(SyncItemKind.Capture, line.Properties["Kind"]);
        Assert.Equal("c-1", line.Properties["ItemId"]);
        Assert.Equal("Buy milk", line.Properties["Title"]);
    }

    /// <summary>The snapshot is a copy: a reader holding one is not walking a
    /// list a writer on another thread is moving.</summary>
    [Fact]
    public void A_snapshot_does_not_change_when_more_is_recorded()
    {
        var log = new SyncActivityLog();
        log.Record(SyncDirection.Sent, SyncItemKind.Task, "a", "First");

        var before = log.Snapshot();
        log.Record(SyncDirection.Sent, SyncItemKind.Task, "b", "Second");

        Assert.Single(before);
        Assert.Equal(2, log.Snapshot().Count);
    }

    private sealed class CapturingLogger : ILogger<SyncActivityLog>
    {
        public List<(LogLevel Level, string Message, Dictionary<string, object?> Properties)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IReadOnlyList<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : [];

            Lines.Add((logLevel, formatter(state, exception), properties));
        }
    }
}
