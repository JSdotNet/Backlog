using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// The open every repository in this project shares. The tables are each
/// repository's own (inherited ADR 0014), so the schema stays with its owner and
/// arrives here as a callback; what is shared is the file and the way into it.
/// </summary>
internal static class SqliteSchema
{
    /// <summary>Creates the database folder and file if they are not there yet,
    /// sets WAL, and runs <paramref name="ensureSchema"/> before handing the
    /// connection back — disposing it if any of that fails. Called on the way into
    /// every operation: the statements are idempotent, and the alternative —
    /// caching which paths have been prepared — would be wrong the first time
    /// somebody moved or deleted the file underneath a running app.</summary>
    internal static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        Func<SqliteConnection, CancellationToken, Task> ensureSchema,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(ConnectionString(databasePath));

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await EnsureAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
            await ensureSchema(connection, cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The connection string every repository opens
    /// <paramref name="databasePath"/> with. Microsoft.Data.Sqlite keys its
    /// pools by the exact string, so whoever clears this database's pool has to
    /// ask with this one — see <see cref="SqliteDatabaseFile.ReleasePool"/>.</summary>
    internal static string ConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

    /// <summary>Runs one batch of idempotent DDL against an open connection.</summary>
    internal static async Task EnsureAsync(SqliteConnection connection, string ddl, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ddl;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
