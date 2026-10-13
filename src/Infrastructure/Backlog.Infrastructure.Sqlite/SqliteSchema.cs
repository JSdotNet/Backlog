using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// The open every repository in this project shares. The tables are each
/// repository's own (inherited ADR 0014), so the schema stays with its owner and
/// arrives here as a <see cref="SqliteSchemaDefinition"/>; what is shared is the
/// file, the way into it, and the stamp that says a file has had an owner's
/// setup already.
/// </summary>
internal static class SqliteSchema
{
    /// <summary>One row per owner: the fingerprint of the setup that last ran
    /// against this file. Not <c>PRAGMA user_version</c>, which is one number per
    /// file — every repository here shares <c>backlog.db</c>, and one number
    /// would have to answer for all of their schemas at once.</summary>
    private const string StampTable = "schema_stamps";

    /// <summary>Creates the database folder and file if they are not there yet,
    /// sets WAL, and brings <paramref name="schema"/> up to date before handing
    /// the connection back — disposing it if any of that fails.
    /// <para>
    /// Called on the way into every operation, but the setup itself runs only
    /// when the file's stamp for this owner is not this build's fingerprint: one
    /// indexed read instead of the DDL, the column probes and the seeding
    /// <c>UPDATE</c>s on every open. The stamp lives in the file rather than in
    /// this process, which is what keeps it right when somebody moves or deletes
    /// the file underneath a running app — a new or replaced file has no stamp,
    /// reads as never set up, and gets the setup again.
    /// </para></summary>
    internal static async Task<SqliteConnection> OpenAsync(
        string databasePath,
        SqliteSchemaDefinition schema,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(ConnectionString(databasePath));

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await EnsureAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

            if (!string.Equals(
                    await ReadStampAsync(connection, schema.Owner, cancellationToken).ConfigureAwait(false),
                    schema.Fingerprint,
                    StringComparison.Ordinal))
            {
                await SetUpAsync(connection, schema, cancellationToken).ConfigureAwait(false);
            }

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

    /// <summary>The whole of an owner's setup, every statement idempotent (local
    /// ADR 0006): the DDL, then each missing column, then the statements that need
    /// those columns — and the stamp last, so a setup that fails halfway leaves
    /// none and runs again on the next open.</summary>
    private static async Task SetUpAsync(
        SqliteConnection connection,
        SqliteSchemaDefinition schema,
        CancellationToken cancellationToken)
    {
        await EnsureAsync(connection, schema.Ddl, cancellationToken).ConfigureAwait(false);

        foreach (var table in schema.Columns.GroupBy(column => column.Table, StringComparer.OrdinalIgnoreCase))
        {
            var present = await ColumnsOfAsync(connection, table.Key, cancellationToken).ConfigureAwait(false);

            foreach (var column in table.Where(column => !present.Contains(column.Name)))
            {
                try
                {
                    await EnsureAsync(
                        connection,
                        $"ALTER TABLE {column.Table} ADD COLUMN {column.Name} {column.Definition};",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
                {
                    // Another connection setting up the same unstamped file added
                    // it between the read above and this ALTER: the column is
                    // there, which is all this step is for.
                }
            }
        }

        foreach (var statement in schema.AfterColumns)
        {
            await EnsureAsync(connection, statement, cancellationToken).ConfigureAwait(false);
        }

        await using var stamp = connection.CreateCommand();
        stamp.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {StampTable} (
                owner       TEXT PRIMARY KEY NOT NULL,
                fingerprint TEXT NOT NULL
            );
            INSERT INTO {StampTable} (owner, fingerprint) VALUES ($owner, $fingerprint)
            ON CONFLICT(owner) DO UPDATE SET fingerprint = excluded.fingerprint;
            """;
        stamp.Parameters.AddWithValue("$owner", schema.Owner);
        stamp.Parameters.AddWithValue("$fingerprint", schema.Fingerprint);
        await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The fingerprint the file was last set up with for
    /// <paramref name="owner"/>, or null when it never was — including a file with
    /// no stamp table at all, which is every file a build before the stamp wrote
    /// and every file created since this process last looked.</summary>
    private static async Task<string?> ReadStampAsync(
        SqliteConnection connection,
        string owner,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT fingerprint FROM {StampTable} WHERE owner = $owner;";
        command.Parameters.AddWithValue("$owner", owner);

        try
        {
            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1
                                          && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // The stamp table is not there yet. A catch rather than a sqlite_master
            // probe first, so the ordinary open — stamped — is one statement and
            // only a new file pays for this.
            return null;
        }
    }

    /// <summary>Every column <paramref name="table"/> has, read once — one
    /// <c>PRAGMA table_info</c> per table, however many columns are checked
    /// against it.</summary>
    private static async Task<HashSet<string>> ColumnsOfAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var probe = connection.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await probe.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var nameOrdinal = reader.GetOrdinal("name");
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(nameOrdinal));
        }

        return columns;
    }
}

/// <summary>A column added to a table that may already have rows: additive, and
/// nullable or defaulted (local ADR 0006). Every part is a compile-time constant
/// from the owning repository and never anything a caller supplies, which is
/// what makes composing the DDL by string safe.</summary>
internal sealed record SqliteColumn(string Table, string Name, string Definition);

/// <summary>
/// One repository's setup, written as data so the stamp can be derived from it.
/// <para>
/// <see cref="Fingerprint"/> is a hash of every statement here, so the setup
/// reruns on a file stamped by any other version of it — a column added, an
/// index added, a seed changed — without anybody having to remember to raise a
/// version number. A number somebody forgets passes every test, because tests
/// start from a new file; it fails only on the files people already have.
/// </para>
/// </summary>
internal sealed class SqliteSchemaDefinition
{
    /// <param name="owner">The stamp's key — one per repository sharing the file.</param>
    /// <param name="ddl">The <c>IF NOT EXISTS</c> tables and indexes, run first.</param>
    /// <param name="columns">Columns added after their table already had rows.</param>
    /// <param name="afterColumns">Statements that need those columns — seeds,
    /// rewrites of a retired value, an index over an added column — run after them,
    /// in order. Each idempotent by construction (local ADR 0006).</param>
    public SqliteSchemaDefinition(
        string owner,
        string ddl,
        IReadOnlyList<SqliteColumn>? columns = null,
        IReadOnlyList<string>? afterColumns = null)
    {
        Owner = owner;
        Ddl = ddl;
        Columns = columns ?? [];
        AfterColumns = afterColumns ?? [];
        Fingerprint = ComputeFingerprint();
    }

    public string Owner { get; }

    public string Ddl { get; }

    public IReadOnlyList<SqliteColumn> Columns { get; }

    public IReadOnlyList<string> AfterColumns { get; }

    public string Fingerprint { get; }

    private string ComputeFingerprint()
    {
        // Unit separators between the parts, so no two different definitions
        // concatenate to the same text.
        var text = new StringBuilder(Ddl).Append('\u001f');
        foreach (var column in Columns)
        {
            text.Append(column.Table).Append('\u001e').Append(column.Name).Append('\u001e').Append(column.Definition).Append('\u001f');
        }

        text.Append('\u001d');
        foreach (var statement in AfterColumns)
        {
            text.Append(statement).Append('\u001f');
        }

        // Raw string literals take the source file's line endings, so a CRLF
        // checkout and an LF one would otherwise stamp the same setup differently.
        var normalized = text.Replace("\r\n", "\n").ToString();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
