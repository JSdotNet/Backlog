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

    [Fact]
    public void A_new_file_keeps_no_roadmap_plan()
    {
        Assert.Null(new SqliteTaskViewStore(_path).ReadRoadmapPlan());
    }

    /// <summary>Local ADR 0062 §8: the roadmap plan document is kept whole with the page that
    /// carried it, beside the preferences, and comes back after a restart.</summary>
    [Fact]
    public async Task The_roadmap_plan_survives_a_new_store_beside_the_preferences_and_cursor_of_its_page()
    {
        var preferences = new MyDayPreferencesDocument(T0, 3, """{"clearsOvernight":false}""");
        var plan = new RoadmapPlanCopy(T0.AddMinutes(1), 4, """{"version":1,"items":[{"tag":"lets-bbq","myDayOn":"2026-09-25"}],"later":true}""");

        await new SqliteTaskViewStore(_path).SaveAsync([], "pos:4", preferences, plan, TestContext.Current.CancellationToken);

        var reopened = new SqliteTaskViewStore(_path);

        Assert.Equal(plan, reopened.ReadRoadmapPlan());
        Assert.Equal(preferences, reopened.ReadMyDayPreferences());
        Assert.Equal("pos:4", reopened.ReadCursor());
    }

    [Fact]
    public async Task A_later_save_replaces_the_one_roadmap_plan_and_a_save_without_one_keeps_it()
    {
        var store = new SqliteTaskViewStore(_path);
        var first = new RoadmapPlanCopy(T0, 1, """{"items":[]}""");
        var second = new RoadmapPlanCopy(T0.AddMinutes(1), 2, """{"items":[{"tag":"a","myDayOn":"2026-09-25"}]}""");

        await store.SaveAsync([], "pos:1", null, first, TestContext.Current.CancellationToken);
        await store.SaveAsync([], "pos:2", null, second, TestContext.Current.CancellationToken);
        await store.SaveAsync([], "pos:3", new MyDayPreferencesDocument(T0, 3, "{}"), TestContext.Current.CancellationToken);

        Assert.Equal(second, store.ReadRoadmapPlan());
    }

    /// <summary>A phone upgraded from a build that folded the roadmap plan away pulls the
    /// feed from the start once — its cursor had already passed the plan document — and
    /// keeps every row and its preferences while it does.</summary>
    [Fact]
    public async Task A_file_from_before_the_roadmap_plan_table_forgets_its_cursor_once_and_keeps_its_rows()
    {
        var task = TestTasks.Task("Book the train", T0);
        var preferences = new MyDayPreferencesDocument(T0, 1, """{"clearsOvernight":false}""");
        await new SqliteTaskViewStore(_path).SaveAsync([new TaskViewRow(task.Id, task.UpdatedAt, null, 1, task.Task)], "pos:40", preferences, TestContext.Current.CancellationToken);
        DropTable("roadmap_plan");

        var upgraded = new SqliteTaskViewStore(_path);

        Assert.Null(upgraded.ReadCursor());
        Assert.Single(upgraded.ReadRows());
        Assert.Equal(preferences, upgraded.ReadMyDayPreferences());

        await upgraded.SaveAsync([], "pos:41", TestContext.Current.CancellationToken);

        Assert.Equal("pos:41", new SqliteTaskViewStore(_path).ReadCursor());
    }

    /// <summary>Takes the file back to the shape a build before ADR 0032 left it in.</summary>
    private void DropPreferencesTable() => DropTable("my_day_preferences");

    private void DropTable(string table)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE {table}";
        command.ExecuteNonQuery();
    }
}
