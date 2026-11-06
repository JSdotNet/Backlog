using Backlog.Infrastructure.Devbook;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>One outline row of the generated database: what the writer
/// derived by the convention, with the title it read from the
/// Markdown.</summary>
internal sealed record DevbookMenuOutlineRow(string Type, string Name, string Path, string Title, bool IsRoot, string? Parent = null);

/// <summary>The generated devbook database the menu tests stand behind a
/// folder, so the rail's titles can be proved without the background
/// builder. Shared by every menu test that needs one; a test using it joins
/// <see cref="SqlitePoolClearingCollection"/>.</summary>
internal static class DevbookMenuTestDatabase
{
    /// <summary>
    /// A generated devbook database for the repository, describing
    /// <paramref name="scope"/>, built from the writer's own DDL in
    /// <c>tools/devbook/devbook-schema.sql</c> — nothing on the C# side
    /// restates that schema, fixtures included.
    /// </summary>
    public static void Write(string repositoryRoot, string scope, IReadOnlyList<DevbookMenuOutlineRow> rows)
    {
        var databasePath = DevbookDatabaseLocation.ForRepositoryRoot(repositoryRoot)!;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, DevbookDatabaseSchema.Ddl);
        Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{DevbookDatabaseSchema.Version}')");
        Execute(connection, "INSERT INTO meta (key, value) VALUES ('generatedAt', '2026-09-08T01:23:45.000Z')");

        var ids = new Dictionary<string, long>(StringComparer.Ordinal);
        var ordinal = 0;

        foreach (var row in rows)
        {
            var parent = row.Parent is not null && ids.TryGetValue(row.Parent, out var parentId)
                ? parentId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "NULL";

            Execute(connection, $"""
                INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
                VALUES ('{scope}', {parent}, {ordinal++}, '{row.Type}', '{row.Name}', '{row.Path}',
                        '{row.Title.Replace("'", "''")}', 'active', 'domain', {(row.IsRoot ? 1 : 0)})
                """);

            ids[row.Path] = Scalar(connection, "SELECT last_insert_rowid()");

            if (row.Type != "file") continue;

            // The drift facts, so the rail trusts the row instead of treating the
            // chapter as one the writer never saw.
            var file = new FileInfo(Path.Combine(repositoryRoot, row.Path.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.FullName)));
            var mtime = (long)Math.Round(
                (double)(new DateTimeOffset(file.LastWriteTimeUtc).UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Execute(connection, $"""
                INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
                VALUES ('{row.Path}', 'domain', 'slug', 1, '{row.Title.Replace("'", "''")}', 'active', 1, 'text', 'text', 'aa', '{hash}', {file.Length}, {mtime})
                """);
        }

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
