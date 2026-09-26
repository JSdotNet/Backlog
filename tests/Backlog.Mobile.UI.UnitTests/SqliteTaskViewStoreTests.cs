using Backlog.Mobile.UI.Tasks;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>The fold survives the app closing: rows, tombstones and the cursor come
/// back from the file as they went in, beside the outbox in the same file.</summary>
public sealed class SqliteTaskViewStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 8, 0, 0, TimeSpan.Zero);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"task-view-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task Rows_and_the_cursor_survive_a_new_store_over_the_same_file()
    {
        var live = TestTasks.Task("Book the train", T0, inMyDayOn: new DateOnly(2026, 9, 25), tags: ["travel"]);
        var gone = TestTasks.Task("Old", T0, deletedAt: T0.AddMinutes(1));

        await new SqliteTaskViewStore(_path).SaveAsync(
            [
                new TaskViewRow(live.Id, live.UpdatedAt, null, 3, live.Task),
                new TaskViewRow(gone.Id, gone.UpdatedAt, gone.DeletedAt, 4, gone.Task)
            ],
            "pos:2",
            TestContext.Current.CancellationToken);

        // The outbox's own tables in the same file do not get in the way.
        _ = new Outbox.SqliteDeviceStore(_path);
        var reopened = new SqliteTaskViewStore(_path);

        var rows = reopened.ReadRows().ToDictionary(row => row.Id);
        Assert.Equal("pos:2", reopened.ReadCursor());
        Assert.Equal(new DateOnly(2026, 9, 25), rows[live.Id].Task.InMyDayOn);
        Assert.Equal(["travel"], rows[live.Id].Task.Tags);
        Assert.Equal(3, rows[live.Id].ServerTimestamp);
        Assert.Equal(T0, rows[live.Id].UpdatedAt);
        Assert.Equal(T0.AddMinutes(1), rows[gone.Id].DeletedAt);
    }

    [Fact]
    public async Task A_save_without_a_cursor_keeps_the_one_there_and_a_reset_forgets_it()
    {
        var store = new SqliteTaskViewStore(_path);
        var change = TestTasks.Task("Added here", T0);

        await store.SaveAsync([], "pos:9", TestContext.Current.CancellationToken);
        await store.SaveAsync([new TaskViewRow(change.Id, change.UpdatedAt, null, 0, change.Task)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("pos:9", store.ReadCursor());

        await store.ResetCursorAsync(TestContext.Current.CancellationToken);

        Assert.Null(store.ReadCursor());
        Assert.Single(store.ReadRows());
    }
}
