using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The writing side of the contract, read from the file that owns it.
///
/// <para><c>tools/devbook/devbook-schema.sql</c> holds the DDL both writers load
/// — the Node writer reads it, the app's builder embeds it (local ADR 0015). Every
/// database in this suite is created from the file on disk, so a column renamed
/// there fails a test here, and <c>DevbookSchemaContractTests</c> pins the
/// embedded copy to the same text.</para>
///
/// <para>This is the same pairing <c>DiagramSourceHash.Normalize</c> and
/// <c>normalizeDiagramSource</c> already have, and it exists for the same reason:
/// the failure it prevents is invisible from inside either language.</para>
/// </summary>
internal static class DevbookSchemaSource
{
    private static readonly string[] SchemaFile = ["tools", "devbook", "devbook-schema.sql"];

    private static readonly Lazy<string> Text = new(() => File.ReadAllText(RepositoryRoot.File(SchemaFile)));

    /// <summary>The path of the file this contract is read from, for a failure
    /// message that names where to look.</summary>
    public static string Path => RepositoryRoot.Combine(SchemaFile);

    /// <summary>The schema file, verbatim.</summary>
    public static string Ddl => Text.Value;

    /// <summary>The file's <c>-- schema-version: N</c> line.</summary>
    public static int Version
    {
        get
        {
            var match = Regex.Match(Text.Value, @"^-- schema-version: (?<value>\d+)\s*$", RegexOptions.Multiline);
            Assert.True(match.Success, $"{Path} no longer carries a `-- schema-version: N` line.");
            return int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A database created from that DDL, at <paramref name="path"/>, with
    /// <c>schemaVersion</c> already recorded — the state the generator leaves
    /// behind before it inserts a single row.
    /// </summary>
    public static void Create(string path, int? schemaVersion = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();

        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, Ddl);

        if (schemaVersion is { } version)
        {
            Execute(
                connection,
                $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{version.ToString(CultureInfo.InvariantCulture)}')");
        }

        // The generator closes its connection before renaming the file into place,
        // which checkpoints the write-ahead log and removes the sidecars. A test
        // that left them behind would be testing a state no reader ever meets.
        SqliteConnection.ClearPool(connection);
    }

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Opens a database this suite created, to fill it.</summary>
    public static SqliteConnection OpenForWriting(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite
        }.ToString());

        connection.Open();
        return connection;
    }
}

/// <summary>A repository root and the database the resolver keys to it, both
/// deleted when the test that made them is done.</summary>
internal sealed class TemporaryDatabase : IDisposable
{
    public TemporaryDatabase()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "backlog-devbook-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);
        DatabaseFile = DevbookDatabaseLocation.ForRepositoryRoot(RootDirectory)
            ?? throw new InvalidOperationException("The test storage folder is not configured.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabaseFile)!);
    }

    /// <summary>The repository root, so a test can hand a consumer a knowledge
    /// folder path the way the app does.</summary>
    public string RootDirectory { get; }

    /// <summary>The database itself, where the resolver says this root's is:
    /// under the suite's storage folder, never inside the root (local ADR 0015).</summary>
    public string DatabaseFile { get; }

    /// <summary>A knowledge folder inside this root, created on disk.</summary>
    public string Folder(string name)
    {
        var folder = Path.Combine(RootDirectory, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>The absolute path of a repository-relative file under this root.</summary>
    public string Resolve(string relativePath) =>
        Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
            var databaseFolder = System.IO.Path.GetDirectoryName(DatabaseFile);
            if (databaseFolder is not null && Directory.Exists(databaseFolder)) Directory.Delete(databaseFolder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
