using Backlog.Infrastructure.Sync.DevbookSubjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>This device's devbook subject progress across a restart — the cases
/// <see cref="FileAnnotationSyncStateStoreTests"/> pins, over the subject
/// file.</summary>
public sealed class FileDevbookSubjectSyncStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "backlog-devbook-subject-sync-state-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_saved_state_is_read_back_by_the_next_run()
    {
        var path = Path.Combine(_root, "devbook-subject-sync-state.json");
        var owner = Guid.NewGuid();

        new FileDevbookSubjectSyncStateStore(path).Save(new DevbookSubjectSyncState(Noon, "cursor-1", owner));

        var reopened = new FileDevbookSubjectSyncStateStore(path);

        Assert.Equal(Noon, reopened.Current.PushWatermark);
        Assert.Equal("cursor-1", reopened.Current.PullCursor);
        Assert.Equal(owner, reopened.Current.OwnerId);
    }

    [Fact]
    public void A_store_with_no_file_has_got_nowhere()
    {
        var store = new FileDevbookSubjectSyncStateStore(Path.Combine(_root, "never-written.json"));

        Assert.Equal(DateTimeOffset.MinValue, store.Current.PushWatermark);
        Assert.Null(store.Current.PullCursor);
    }

    [Fact]
    public void A_file_nobody_can_read_has_got_nowhere_rather_than_failing()
    {
        var path = Path.Combine(_root, "devbook-subject-sync-state.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(path, "{ not json");

        var store = new FileDevbookSubjectSyncStateStore(path);

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
