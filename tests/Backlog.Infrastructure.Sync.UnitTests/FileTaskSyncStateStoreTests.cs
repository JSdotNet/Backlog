namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// This device's progress, across a restart. The interesting cases are the two
/// that are not a round trip: a file that is not there and a file that cannot be
/// read both have to answer "got nowhere" rather than throw, because starting
/// over is the only recovery from a lost progress marker and starting over is
/// safe — a whole-document upsert has nothing to do differently the second time.
/// </summary>
public sealed class FileTaskSyncStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-task-sync-state-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_saved_state_is_read_back_by_the_next_run()
    {
        var path = Path.Combine(_root, "task-sync-state.json");

        new FileTaskSyncStateStore(path).Save(new TaskSyncState(Noon, "cursor-1"));

        var reopened = new FileTaskSyncStateStore(path);

        Assert.Equal(Noon, reopened.Current.PushWatermark);
        Assert.Equal("cursor-1", reopened.Current.PullCursor);
    }

    [Fact]
    public void Saving_raises_changed_and_moves_current()
    {
        var store = new FileTaskSyncStateStore(Path.Combine(_root, "task-sync-state.json"));
        var raised = 0;
        store.Changed += () => raised++;

        store.Save(new TaskSyncState(Noon, "cursor-1"));

        Assert.Equal(1, raised);
        Assert.Equal("cursor-1", store.Current.PullCursor);
    }

    /// <summary>A device that has never synced has got nowhere, and the minimum
    /// instant is what makes the push predicate select everything without a case
    /// for "no watermark yet".</summary>
    [Fact]
    public void A_store_with_no_file_has_got_nowhere()
    {
        var store = new FileTaskSyncStateStore(Path.Combine(_root, "never-written.json"));

        Assert.Equal(DateTimeOffset.MinValue, store.Current.PushWatermark);
        Assert.Null(store.Current.PullCursor);
    }

    [Fact]
    public void The_folder_is_created_for_a_path_that_does_not_exist_yet()
    {
        var path = Path.Combine(_root, "nested", "deeper", "task-sync-state.json");

        var store = new FileTaskSyncStateStore(path);
        store.Save(new TaskSyncState(Noon, "cursor-1"));

        Assert.True(File.Exists(path));
    }

    /// <summary>Half-written, hand-edited, or left by a format nobody kept. It
    /// reads as "got nowhere" rather than as an error, exactly as an unreadable
    /// credential envelope reads as "not paired".</summary>
    [Fact]
    public void A_file_that_cannot_be_read_has_got_nowhere()
    {
        var path = Path.Combine(_root, "task-sync-state.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ this is not json");

        var store = new FileTaskSyncStateStore(path);

        Assert.Equal(DateTimeOffset.MinValue, store.Current.PushWatermark);
        Assert.Null(store.Current.PullCursor);
    }

    /// <summary>And a corrupt file is not a dead end: saving over it works, so
    /// the next run reads the progress rather than the wreckage.</summary>
    [Fact]
    public void A_corrupt_file_is_replaced_by_the_next_save()
    {
        var path = Path.Combine(_root, "task-sync-state.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ this is not json");

        new FileTaskSyncStateStore(path).Save(new TaskSyncState(Noon, "cursor-1"));

        Assert.Equal("cursor-1", new FileTaskSyncStateStore(path).Current.PullCursor);
    }

    /// <summary>Nothing here is a secret, so the file is readable text rather
    /// than an envelope — which is the decision worth pinning, because the
    /// credential store beside it made the opposite one.</summary>
    [Fact]
    public void The_progress_is_written_as_plain_json()
    {
        var path = Path.Combine(_root, "task-sync-state.json");

        new FileTaskSyncStateStore(path).Save(new TaskSyncState(Noon, "cursor-1"));

        Assert.Contains("cursor-1", File.ReadAllText(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
