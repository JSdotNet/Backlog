using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// The open every repository in this project shares: the folder made if it is
/// missing, the file created if it is new, WAL set, and the repository's own
/// schema brought up to date before the connection is handed back — once per
/// file and per version of that schema, which the stamp in the file decides.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class SqliteSchemaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqlite-schema-tests-" + Guid.NewGuid().ToString("N"));

    private string DatabasePath => Path.Combine(_dir, "backlog.db");

    private static readonly SqliteSchemaDefinition NoSchema = new("none", "SELECT 1;");

    /// <summary>A setup whose every run leaves a row behind, so a test can count
    /// how often it ran. <paramref name="variant"/> changes the definition, and
    /// with it the fingerprint, without changing what it does.</summary>
    private static SqliteSchemaDefinition Counting(string owner = "probe", string variant = "") => new(
        owner,
        $"CREATE TABLE IF NOT EXISTS {owner}_runs (n INTEGER); -- {variant}",
        afterColumns: [$"INSERT INTO {owner}_runs (n) VALUES (1);"]);

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
        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, NoSchema, TestContext.Current.CancellationToken);

        Assert.Equal("wal", await ScalarAsync<string>(connection, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task A_fresh_file_is_set_up_and_stamped_before_the_connection_is_returned()
    {
        var schema = Counting();

        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, schema, TestContext.Current.CancellationToken);

        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM probe_runs;"));
        Assert.Equal(schema.Fingerprint, await ScalarAsync<string>(connection, "SELECT fingerprint FROM schema_stamps WHERE owner = 'probe';"));
    }

    [Fact]
    public async Task A_file_already_stamped_with_this_schema_is_not_set_up_again()
    {
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting(), TestContext.Current.CancellationToken)) { }
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting(), TestContext.Current.CancellationToken)) { }
        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, Counting(), TestContext.Current.CancellationToken);

        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM probe_runs;"));
    }

    /// <summary>The fingerprint is the definition, so any change to it — a column,
    /// an index, a seed — reruns the setup on a file stamped by the old one,
    /// without anybody remembering to raise a number.</summary>
    [Fact]
    public async Task A_changed_schema_runs_again_on_a_file_stamped_by_the_old_one()
    {
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting(variant: "v1"), TestContext.Current.CancellationToken)) { }
        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, Counting(variant: "v2"), TestContext.Current.CancellationToken);

        Assert.Equal(2, await ScalarAsync<long>(connection, "SELECT count(*) FROM probe_runs;"));
        Assert.Equal(
            Counting(variant: "v2").Fingerprint,
            await ScalarAsync<string>(connection, "SELECT fingerprint FROM schema_stamps WHERE owner = 'probe';"));
    }

    /// <summary>What the old code comment feared from a cache: a file deleted and
    /// created again under a running app. The stamp is in the file, so the new
    /// file has none and is set up again.</summary>
    [Fact]
    public async Task A_file_deleted_and_recreated_under_the_same_path_is_set_up_again()
    {
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting(), TestContext.Current.CancellationToken)) { }

        DeleteDatabase();

        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, Counting(), TestContext.Current.CancellationToken);

        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM probe_runs;"));
    }

    /// <summary>Every repository shares one file, so each keeps its own stamp: one
    /// owner's setup neither satisfies nor resets another's.</summary>
    [Fact]
    public async Task Owners_sharing_a_file_keep_separate_stamps()
    {
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting("first"), TestContext.Current.CancellationToken)) { }
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting("second"), TestContext.Current.CancellationToken)) { }
        await using (await SqliteSchema.OpenAsync(DatabasePath, Counting("first"), TestContext.Current.CancellationToken)) { }
        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, Counting("second"), TestContext.Current.CancellationToken);

        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM first_runs;"));
        Assert.Equal(1, await ScalarAsync<long>(connection, "SELECT count(*) FROM second_runs;"));
    }

    [Fact]
    public async Task Missing_columns_are_added_to_a_table_that_already_exists()
    {
        await using (var seed = new SqliteConnection(SqliteSchema.ConnectionString(DatabasePath)))
        {
            Directory.CreateDirectory(_dir);
            await seed.OpenAsync(TestContext.Current.CancellationToken);
            await SqliteSchema.EnsureAsync(seed, "CREATE TABLE things (id TEXT PRIMARY KEY, kept TEXT);", TestContext.Current.CancellationToken);
        }

        var schema = new SqliteSchemaDefinition(
            "things",
            "CREATE TABLE IF NOT EXISTS things (id TEXT PRIMARY KEY, kept TEXT, added TEXT NULL, defaulted TEXT NOT NULL DEFAULT '[]');",
            columns:
            [
                new("things", "kept", "TEXT"),
                new("things", "added", "TEXT NULL"),
                new("things", "defaulted", "TEXT NOT NULL DEFAULT '[]'"),
            ]);

        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, schema, TestContext.Current.CancellationToken);

        Assert.Equal(4, await ScalarAsync<long>(connection, "SELECT count(*) FROM pragma_table_info('things');"));
    }

    /// <summary>The stamp is written last, so a setup that fails part-way leaves
    /// none and runs again — whole — on the next open.</summary>
    [Fact]
    public async Task A_failing_setup_leaves_no_stamp_and_rethrows()
    {
        var failing = new SqliteSchemaDefinition(
            "broken",
            "CREATE TABLE IF NOT EXISTS broken_runs (n INTEGER);",
            afterColumns: ["INSERT INTO nowhere (n) VALUES (1);"]);

        await Assert.ThrowsAsync<SqliteException>(() => SqliteSchema.OpenAsync(DatabasePath, failing, TestContext.Current.CancellationToken));

        await using var connection = await SqliteSchema.OpenAsync(DatabasePath, NoSchema, TestContext.Current.CancellationToken);
        Assert.Equal(0, await ScalarAsync<long>(connection, "SELECT count(*) FROM schema_stamps WHERE owner = 'broken';"));
    }

    [Fact]
    public void The_fingerprint_does_not_depend_on_the_source_file_s_line_endings()
    {
        var lf = new SqliteSchemaDefinition("x", "CREATE TABLE a (\n  id TEXT\n);");
        var crlf = new SqliteSchemaDefinition("x", "CREATE TABLE a (\r\n  id TEXT\r\n);");

        Assert.Equal(lf.Fingerprint, crlf.Fingerprint);
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(DatabasePath + suffix);
        }
    }

    public void Dispose()
    {
        // The pool can still hold the file open the instant a test ends.
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
