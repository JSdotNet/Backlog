using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// A task's waiting-for in the local store: two columns beside the task, brought
/// into a database an earlier build created by the additive schema step (local
/// ADR 0006).
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteTaskWaitingForTests : IDisposable
{
    private readonly string _root;
    private readonly SqliteTaskRepository _repository;

    public SqliteTaskWaitingForTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "backlog-sqlite-tests", Guid.NewGuid().ToString("n"));
        _repository = new SqliteTaskRepository(_root);
    }

    [Fact]
    public async Task A_waiting_for_is_saved_changed_and_cleared()
    {
        var task = new TaskItem("Fix the roof", string.Empty, EntryType.Task);
        task.SetWaitingFor(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));

        await _repository.SaveAsync(task, TestContext.Current.CancellationToken);
        var loaded = await _repository.GetAsync(task.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)), loaded.WaitingFor);

        loaded.SetWaitingFor(new WaitingFor("Anna"));
        await _repository.SaveAsync(loaded, TestContext.Current.CancellationToken);
        var changed = await _repository.GetAsync(task.Id, TestContext.Current.CancellationToken);
        Assert.Equal(new WaitingFor("Anna"), changed!.WaitingFor);

        changed.SetWaitingFor(null);
        await _repository.SaveAsync(changed, TestContext.Current.CancellationToken);

        Assert.Null((await _repository.GetAsync(task.Id, TestContext.Current.CancellationToken))!.WaitingFor);
    }

    /// <summary>A database from before the two columns opens, reads its rows as
    /// waiting on nobody, and takes a waiting-for once the step has added them.</summary>
    [Fact]
    public async Task A_database_written_before_the_waiting_for_columns_still_opens_and_reads()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(_root);

        await using (var seed = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = _repository.DatabasePath }.ToString()))
        {
            await seed.OpenAsync(TestContext.Current.CancellationToken);

            await using var create = seed.CreateCommand();
            create.CommandText = """
                CREATE TABLE tasks (
                    id TEXT PRIMARY KEY NOT NULL, title TEXT NOT NULL, content_md TEXT NOT NULL DEFAULT '',
                    type TEXT NOT NULL, status TEXT NOT NULL, priority TEXT NOT NULL,
                    sort_order INTEGER NOT NULL DEFAULT 0, area TEXT NULL, created_at TEXT NOT NULL,
                    source_inbox_id TEXT NULL, recurrence_source_id TEXT NULL, due_on TEXT NULL,
                    remind_at TEXT NULL, recurrence TEXT NULL, in_my_day_on TEXT NULL, view TEXT NULL,
                    tags TEXT NOT NULL DEFAULT '[]', repo_ids TEXT NOT NULL DEFAULT '[]',
                    depends_on TEXT NOT NULL DEFAULT '[]', sub_items TEXT NOT NULL DEFAULT '[]',
                    usage_events TEXT NOT NULL DEFAULT '[]', projections TEXT NOT NULL DEFAULT '[]',
                    effort INTEGER NULL, import_plan_id TEXT NULL, import_item_id TEXT NULL,
                    updated_at TEXT NULL, deleted_at TEXT NULL, attachment_path TEXT NULL,
                    completed_on TEXT NULL, started_on TEXT NULL, devbook_refs TEXT NOT NULL DEFAULT '[]',
                    blocked_since TEXT NULL, source_ref TEXT NULL, agenda_at TEXT NULL,
                    agenda_minutes INTEGER NULL, planned_hours TEXT NOT NULL DEFAULT '[]',
                    source_notification_id TEXT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await using var insert = seed.CreateCommand();
            insert.CommandText = """
                INSERT INTO tasks (id, title, type, status, priority, created_at, updated_at)
                VALUES ($id, $title, 'task', 'ready', 'high', $created_at, $created_at);
                """;
            insert.Parameters.AddWithValue("$id", id.ToString());
            insert.Parameters.AddWithValue("$title", "Written before waiting-for existed");
            insert.Parameters.AddWithValue(
                "$created_at",
                createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var loaded = await _repository.GetAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("Written before waiting-for existed", loaded.Title);
        Assert.Null(loaded.WaitingFor);

        loaded.SetWaitingFor(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)));
        await _repository.SaveAsync(loaded, TestContext.Current.CancellationToken);
        var again = await _repository.GetAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(new WaitingFor("Jansen Roofing", new DateOnly(2026, 11, 20)), again!.WaitingFor);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
