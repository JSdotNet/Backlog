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

    /// <summary>Cleared, the file holds no task row, no cursor and no My Day
    /// preferences — everything it held came from the replica — and the outbox's
    /// tables beside them are left alone.</summary>
    [Fact]
    public async Task Clearing_forgets_every_row_the_cursor_and_the_preferences_but_not_the_outbox()
    {
        var task = TestTasks.Task("Book the train", T0);
        var store = new SqliteTaskViewStore(_path);
        await store.SaveAsync(
            [new TaskViewRow(task.Id, task.UpdatedAt, null, 6, task.Task)],
            "pos:7",
            new MyDayPreferencesDocument(T0, 7, """{"clearsOvernight":false}"""),
            TestContext.Current.CancellationToken);
        var device = new Outbox.SqliteDeviceStore(_path);
        await device.AddAsync(new Outbox.OutboxEntry(Guid.NewGuid(), "capture", "{}", 0, null, T0), TestContext.Current.CancellationToken);

        await store.ClearAsync(TestContext.Current.CancellationToken);

        var reopened = new SqliteTaskViewStore(_path);
        Assert.Empty(reopened.ReadRows());
        Assert.Null(reopened.ReadCursor());
        Assert.Null(reopened.ReadMyDayPreferences());
        Assert.Single(new Outbox.SqliteDeviceStore(_path).ReadOutbox());
    }

    [Fact]
    public void A_new_file_keeps_no_my_day_preferences()
    {
        Assert.Null(new SqliteTaskViewStore(_path).ReadMyDayPreferences());
    }

    /// <summary>ADR 0032 Verification 4: the preferences document is kept with the page
    /// that carried it — its rows and its cursor — and comes back after a restart.</summary>
    [Fact]
    public async Task The_my_day_preferences_survive_a_new_store_beside_the_rows_and_cursor_of_their_page()
    {
        var task = TestTasks.Task("Book the train", T0, inMyDayOn: new DateOnly(2026, 9, 24));
        var preferences = new MyDayPreferencesDocument(T0.AddMinutes(3), 7, """{ "updatedAt": "2026-09-25T08:03:00.0000000+00:00", "clearsOvernight": false, "later": 1 }""");

        await new SqliteTaskViewStore(_path).SaveAsync(
            [new TaskViewRow(task.Id, task.UpdatedAt, null, 6, task.Task)],
            "pos:7",
            preferences,
            TestContext.Current.CancellationToken);

        var reopened = new SqliteTaskViewStore(_path);

        Assert.Equal(preferences, reopened.ReadMyDayPreferences());
        Assert.False(reopened.ReadMyDayPreferences()!.ClearsOvernight);
        Assert.Equal("pos:7", reopened.ReadCursor());
        Assert.Equal(task.Id, Assert.Single(reopened.ReadRows()).Id);
    }

    [Fact]
    public async Task A_later_save_replaces_the_one_preferences_row_and_a_save_without_one_keeps_it()
    {
        var store = new SqliteTaskViewStore(_path);
        var first = new MyDayPreferencesDocument(T0, 1, """{"clearsOvernight":false}""");
        var second = new MyDayPreferencesDocument(T0.AddMinutes(1), 2, """{"clearsOvernight":true}""");

        await store.SaveAsync([], "pos:1", first, TestContext.Current.CancellationToken);
        await store.SaveAsync([], "pos:2", second, TestContext.Current.CancellationToken);
        await store.SaveAsync([], "pos:3", TestContext.Current.CancellationToken);

        Assert.Equal(second, store.ReadMyDayPreferences());
    }

    /// <summary>ADR 0032 Verification 10: a phone upgraded from a build without the
    /// preferences table pulls the feed from the start once — its cursor had already
    /// passed any preferences document — and keeps every row while it does.</summary>
    [Fact]
    public async Task A_file_from_before_the_preferences_table_forgets_its_cursor_once_and_keeps_its_rows()
    {
        var task = TestTasks.Task("Book the train", T0);
        await new SqliteTaskViewStore(_path).SaveAsync([new TaskViewRow(task.Id, task.UpdatedAt, null, 1, task.Task)], "pos:40", TestContext.Current.CancellationToken);
        DropPreferencesTable();

        var upgraded = new SqliteTaskViewStore(_path);

        Assert.Null(upgraded.ReadCursor());
        Assert.Single(upgraded.ReadRows());

        await upgraded.SaveAsync([], "pos:41", TestContext.Current.CancellationToken);

        Assert.Equal("pos:41", new SqliteTaskViewStore(_path).ReadCursor());
    }

    /// <summary>Takes the file back to the shape a build before ADR 0032 left it in.</summary>
    private void DropPreferencesTable()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE my_day_preferences";
        command.ExecuteNonQuery();
    }
}
