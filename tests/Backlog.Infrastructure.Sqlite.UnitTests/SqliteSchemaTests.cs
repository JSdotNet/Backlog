using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The open every repository in this project shares: the folder made if it is
/// missing, the file created if it is new, WAL set, and the repository's own
/// schema run before the connection is handed back. The task, inbox and roadmap
/// repositories each keep their DDL; what they no longer keep is a copy of this.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteSchemaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlite-schema-tests-" + Guid.NewGuid().ToString("N"));

    private static Task NoSchema(SqliteConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;

    [Fact]
    public async Task A_missing_parent_folder_is_created()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "backlog.db");

        await using var connection = await SqliteSchema.OpenAsync(path, NoSchema, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task The_connection_comes_back_in_wal_mode()
    {
        var path = Path.Combine(_dir, "backlog.db");

        await using var connection = await SqliteSchema.OpenAsync(path, NoSchema, TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        var mode = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal("wal", mode);
    }

    [Fact]
    public async Task The_repository_s_schema_runs_before_the_connection_is_returned()
    {
        var path = Path.Combine(_dir, "backlog.db");

        await using var connection = await SqliteSchema.OpenAsync(
            path,
            (open, cancellationToken) => SqliteSchema.EnsureAsync(open, "CREATE TABLE IF NOT EXISTS probe (id TEXT PRIMARY KEY NOT NULL);", cancellationToken),
            TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'probe';";
        var count = (long?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task The_schema_runs_on_every_open_rather_than_once_per_path()
    {
        var path = Path.Combine(_dir, "backlog.db");
        var runs = 0;

        Task Counting(SqliteConnection connection, CancellationToken cancellationToken)
        {
            runs++;
            return Task.CompletedTask;
        }

        await using (await SqliteSchema.OpenAsync(path, Counting, TestContext.Current.CancellationToken)) { }
        await using (await SqliteSchema.OpenAsync(path, Counting, TestContext.Current.CancellationToken)) { }

        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task A_failing_schema_disposes_the_connection_and_rethrows()
    {
        var path = Path.Combine(_dir, "backlog.db");
        SqliteConnection? seen = null;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteSchema.OpenAsync(
            path,
            (connection, _) =>
            {
                seen = connection;
                throw new InvalidOperationException("schema failed");
            },
            TestContext.Current.CancellationToken));

        Assert.Equal("schema failed", failure.Message);
        Assert.NotNull(seen);
        Assert.Equal(System.Data.ConnectionState.Closed, seen.State);
    }

    public void Dispose()
    {
        // The pool can still hold the file open the instant a test ends.
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
