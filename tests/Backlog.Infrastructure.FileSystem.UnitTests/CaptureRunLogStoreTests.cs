using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What past capture runs said, on disk: nothing before a run, each source's
/// last line after one, the runs before it newest first, and all of it still
/// there after a restart. Real temp directories, for the reason
/// <see cref="CaptureSourcesSettingsStoreTests"/> gives: the file is the thing
/// under test.
/// </summary>
public class CaptureRunLogStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "capture-run-log-store-tests-" + Guid.NewGuid().ToString("N"));

    public CaptureRunLogStoreTests() => Directory.CreateDirectory(_root);

    private string LogFile => Path.Combine(_root, "capture-runs.json");

    private CaptureRunLogStore Store() => new(LogFile);

    [Fact]
    public void An_untouched_store_has_no_last_run_for_any_source()
    {
        var store = Store();

        Assert.All(CaptureSourceKinds.Monitorable, kind =>
        {
            Assert.Null(store.LastRunFor(kind));
            Assert.Empty(store.EntriesFor(kind));
        });
    }

    [Fact]
    public void A_recorded_run_is_each_sources_last_run()
    {
        var store = Store();

        store.Record(Run(Noon,
            new CaptureRunSourceResult(CaptureSourceKind.YouTube, 2, "YouTube: 2 new items."),
            new CaptureRunSourceResult(CaptureSourceKind.Website, 0, "Website: 0 new items · https://x: no feed found.")));

        Assert.Equal(new CaptureRunLogEntry(Noon, 2, "YouTube: 2 new items."), store.LastRunFor(CaptureSourceKind.YouTube));
        Assert.Equal(
            new CaptureRunLogEntry(Noon, 0, "Website: 0 new items · https://x: no feed found."),
            store.LastRunFor(CaptureSourceKind.Website));
        // Email was not in the run, so it has not been looked at.
        Assert.Null(store.LastRunFor(CaptureSourceKind.Email));
    }

    /// <summary>A source switched off for a run is not in that run; its last
    /// line stays the one from when it was on.</summary>
    [Fact]
    public void A_source_left_out_of_a_later_run_keeps_its_earlier_last_run()
    {
        var store = Store();
        store.Record(Run(Noon, new CaptureRunSourceResult(CaptureSourceKind.Email, 1, "Email: 1 new item.")));

        store.Record(Run(Noon.AddHours(1), new CaptureRunSourceResult(CaptureSourceKind.YouTube, 0, "YouTube: 0 new items.")));

        Assert.Equal(Noon, store.LastRunFor(CaptureSourceKind.Email)!.RanAt);
        Assert.Equal(Noon.AddHours(1), store.LastRunFor(CaptureSourceKind.YouTube)!.RanAt);
    }

    [Fact]
    public void Entries_are_read_newest_first()
    {
        var store = Store();
        store.Record(Run(Noon, new CaptureRunSourceResult(CaptureSourceKind.YouTube, 1, "first")));
        store.Record(Run(Noon.AddMinutes(5), new CaptureRunSourceResult(CaptureSourceKind.YouTube, 0, "second")));
        store.Record(Run(Noon.AddMinutes(10), new CaptureRunSourceResult(CaptureSourceKind.YouTube, 3, "third")));

        Assert.Equal(["third", "second", "first"], store.EntriesFor(CaptureSourceKind.YouTube).Select(entry => entry.Message));
        Assert.Equal("third", store.LastRunFor(CaptureSourceKind.YouTube)!.Message);
    }

    [Fact]
    public void The_log_survives_a_restart()
    {
        Store().Record(Run(Noon, new CaptureRunSourceResult(CaptureSourceKind.Website, 4, "Website: 4 new items.")));

        var reopened = Store();

        Assert.Equal(new CaptureRunLogEntry(Noon, 4, "Website: 4 new items."), reopened.LastRunFor(CaptureSourceKind.Website));
        Assert.Single(reopened.EntriesFor(CaptureSourceKind.Website));
    }

    /// <summary>The log is a window, not an archive: the oldest lines go
    /// once a source has more than the store keeps.</summary>
    [Fact]
    public void Only_the_most_recent_runs_per_source_are_kept()
    {
        var store = Store();

        for (var i = 0; i < CaptureRunLogStore.MaxEntriesPerSource + 5; i++)
        {
            store.Record(Run(Noon.AddMinutes(i), new CaptureRunSourceResult(CaptureSourceKind.YouTube, 0, $"run {i}")));
        }

        var entries = store.EntriesFor(CaptureSourceKind.YouTube);
        Assert.Equal(CaptureRunLogStore.MaxEntriesPerSource, entries.Count);
        Assert.Equal($"run {CaptureRunLogStore.MaxEntriesPerSource + 4}", entries[0].Message);
        Assert.Equal("run 5", entries[^1].Message);

        // The bound holds on disk too, not only in memory.
        Assert.Equal(CaptureRunLogStore.MaxEntriesPerSource, Store().EntriesFor(CaptureSourceKind.YouTube).Count);
    }

    [Fact]
    public void A_run_that_looked_at_nothing_writes_nothing()
    {
        var store = Store();

        store.Record(Run(Noon));

        Assert.All(CaptureSourceKinds.Monitorable, kind => Assert.Null(store.LastRunFor(kind)));
        Assert.False(File.Exists(LogFile));
    }

    [Fact]
    public void Kinds_are_written_as_their_slugs()
    {
        Store().Record(Run(Noon, new CaptureRunSourceResult(CaptureSourceKind.YouTube, 1, "YouTube: 1 new item.")));

        var written = File.ReadAllText(LogFile);

        Assert.Contains("\"youtube\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\": 1", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_nobody_can_read_falls_back_to_an_empty_log()
    {
        File.WriteAllText(LogFile, "{ this is not json");

        var store = Store();

        Assert.All(CaptureSourceKinds.Monitorable, kind => Assert.Null(store.LastRunFor(kind)));
    }

    /// <summary>A hand-edited line for a kind this build cannot read is
    /// dropped rather than guessed at; the ones it can read stay.</summary>
    [Fact]
    public void An_unknown_kind_is_dropped()
    {
        File.WriteAllText(
            LogFile,
            """
            { "sources": [
                { "kind": "rss", "runs": [ { "ranAt": "2026-09-16T12:00:00+00:00", "newItems": 1, "message": "x" } ] },
                { "kind": "email", "runs": [ { "ranAt": "2026-09-16T12:00:00+00:00", "newItems": 1, "message": "Email: 1 new item." } ] }
            ] }
            """);

        var store = Store();

        Assert.Equal("Email: 1 new item.", store.LastRunFor(CaptureSourceKind.Email)!.Message);
        Assert.Null(store.LastRunFor(CaptureSourceKind.YouTube));
    }

    [Fact]
    public void Recording_tells_whoever_is_showing_the_sources()
    {
        var store = Store();
        var announced = 0;
        store.Changed += () => announced++;

        store.Record(Run(Noon, new CaptureRunSourceResult(CaptureSourceKind.Email, 0, "Email: 0 new items.")));
        store.Record(Run(Noon));

        // The empty run is announced too: a panel may be showing "capturing".
        Assert.Equal(2, announced);
    }

    private static CaptureRunResultDto Run(DateTimeOffset ranAt, params CaptureRunSourceResult[] sources) =>
        new(sources, ranAt);

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
