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

    // --- A batch at a time ------------------------------------------------------

    /// <summary>A first sync moves hundreds of documents, and the footer copies
    /// the whole window and redraws on every <c>Changed</c>. A batch recorded at
    /// once is one redraw, however many documents it names.</summary>
    [Fact]
    public void Recording_a_batch_raises_Changed_once()
    {
        var log = new SyncActivityLog();
        var raised = 0;
        log.Changed += () => raised++;

        log.RecordRange(
        [
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "a", "First"),
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "b", "Second"),
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "c", "Third"),
        ]);

        Assert.Equal(1, raised);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public void An_empty_batch_raises_nothing()
    {
        var log = new SyncActivityLog();
        var raised = 0;
        log.Changed += () => raised++;

        log.RecordRange([]);

        Assert.Equal(0, raised);
        Assert.Empty(log.Snapshot());
    }

    /// <summary>The footer reads the window the same way whichever call filled
    /// it: a batch is read back newest first, its last entry on top, above
    /// what was there before.</summary>
    [Fact]
    public void A_batch_reads_back_as_if_its_entries_had_been_recorded_one_by_one()
    {
        var log = new SyncActivityLog();
        log.Record(SyncDirection.Received, SyncItemKind.Task, "earlier", "Earlier");

        log.RecordRange(
        [
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "a", "First"),
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "b", "Second", "deleted"),
            log.Entry(SyncDirection.Sent, SyncItemKind.Task, "c", "Third"),
        ]);

        var entries = log.Snapshot();

        Assert.Equal(["Third", "Second", "First", "Earlier"], entries.Select(entry => entry.Title).ToArray());
        Assert.Equal("deleted", entries[1].Note);
    }

    [Fact]
    public void A_batch_larger_than_the_window_keeps_its_newest_entries()
    {
        var log = new SyncActivityLog();

        log.RecordRange(
        [
            .. Enumerable.Range(0, SyncActivityLog.Capacity + 5)
                .Select(i => log.Entry(SyncDirection.Sent, SyncItemKind.Task, i.ToString(), $"Task {i}")),
        ]);

        var entries = log.Snapshot();

        Assert.Equal(SyncActivityLog.Capacity, entries.Count);
        Assert.Equal(SyncActivityLog.Capacity, log.Count);
        Assert.Equal($"Task {SyncActivityLog.Capacity + 4}", entries[0].Title);
        Assert.Equal("Task 5", entries[^1].Title);
    }

    /// <summary>One redraw, but still one line per document: the dashboard
    /// filters on the lines, and a batch folded into one would hide which
    /// documents it carried.</summary>
    [Fact]
    public void Every_entry_of_a_batch_is_written_to_the_logger()
    {
        var logger = new CapturingLogger();
        var log = new SyncActivityLog(logger);

        log.RecordRange(
        [
            log.Entry(SyncDirection.Received, SyncItemKind.Note, "n-1", "Standup", "edited"),
            log.Entry(SyncDirection.Received, SyncItemKind.Capture, "c-1", "Buy milk"),
        ]);

        Assert.Equal(2, logger.Lines.Count);
        Assert.Equal("n-1", logger.Lines[0].Properties["ItemId"]);
        Assert.Contains("(edited)", logger.Lines[0].Message, StringComparison.Ordinal);
        Assert.Equal("c-1", logger.Lines[1].Properties["ItemId"]);
    }

    /// <summary>An entry built for a batch carries the log's clock, the way
    /// <see cref="SyncActivityLog.Record(SyncDirection, SyncItemKind, string, string, string?)"/>
    /// stamps one.</summary>
    [Fact]
    public void An_entry_the_log_builds_is_stamped_by_its_clock()
    {
        var clock = new FakeTimeProvider(Noon);
        var log = new SyncActivityLog(time: clock);

        var entry = log.Entry(SyncDirection.Sent, SyncItemKind.Session, "s", "A session");

        Assert.Equal(new SyncActivityEntry(Noon, SyncDirection.Sent, SyncItemKind.Session, "s", "A session"), entry);
        Assert.Empty(log.Snapshot());
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
