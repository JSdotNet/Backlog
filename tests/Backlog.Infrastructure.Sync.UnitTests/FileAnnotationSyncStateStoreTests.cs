using Backlog.Infrastructure.Sync.Annotations;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>This device's annotation progress across a restart — the cases
/// <see cref="FileTaskSyncStateStoreTests"/> pins, over the third file.</summary>
public sealed class FileAnnotationSyncStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-annotation-sync-state-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_saved_state_is_read_back_by_the_next_run()
    {
        var path = Path.Combine(_root, "annotation-sync-state.json");

        new FileAnnotationSyncStateStore(path).Save(new AnnotationSyncState(Noon, "cursor-1"));

        var reopened = new FileAnnotationSyncStateStore(path);

        Assert.Equal(Noon, reopened.Current.PushWatermark);
        Assert.Equal("cursor-1", reopened.Current.PullCursor);
    }

    [Fact]
    public void A_store_with_no_file_has_got_nowhere()
    {
        var store = new FileAnnotationSyncStateStore(Path.Combine(_root, "never-written.json"));

        Assert.Equal(DateTimeOffset.MinValue, store.Current.PushWatermark);
        Assert.Null(store.Current.PullCursor);
    }

    [Fact]
    public void A_file_nobody_can_read_has_got_nowhere_rather_than_failing()
    {
        var path = Path.Combine(_root, "annotation-sync-state.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ not json");

        var store = new FileAnnotationSyncStateStore(path);

        Assert.Equal(DateTimeOffset.MinValue, store.Current.PushWatermark);
        Assert.Null(store.Current.PullCursor);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
