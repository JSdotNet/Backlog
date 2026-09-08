using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// The writing side of the contract, read from the file that owns it.
///
/// <para><c>tools/knowledge/knowledge-schema.mjs</c> holds the DDL as one exported
/// string precisely so the reading side can be pinned against it instead of
/// restating it. Every database in this suite is created from that text, so a
/// column renamed there fails a test here — which is the whole of ADR 0004's
/// answer to "a schema written in Node and read in C# can drift silently".</para>
///
/// <para>This is the same pairing <c>DiagramSourceHash.Normalize</c> and
/// <c>normalizeDiagramSource</c> already have, and it exists for the same reason:
/// the failure it prevents is invisible from inside either language.</para>
/// </summary>
internal static class KnowledgeSchemaSource
{
    private static readonly string[] SchemaFile = ["tools", "knowledge", "knowledge-schema.mjs"];

    private static readonly Lazy<string> Text = new(() => File.ReadAllText(RepositoryRoot.File(SchemaFile)));

    /// <summary>The path of the file this contract is read from, for a failure
    /// message that names where to look.</summary>
    public static string Path => RepositoryRoot.Combine(SchemaFile);

    /// <summary>Every statement of <c>KNOWLEDGE_SCHEMA</c>, verbatim.</summary>
    public static string Ddl => Literal("KNOWLEDGE_SCHEMA");

    /// <summary>The writer's <c>SCHEMA_VERSION</c>.</summary>
    public static int Version => int.Parse(Number("SCHEMA_VERSION"), CultureInfo.InvariantCulture);

    /// <summary>The writer's <c>DATABASE_PATH</c>.</summary>
    public static string DatabasePath => Quoted("DATABASE_PATH");

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

    private static string Literal(string name)
    {
        var match = Regex.Match(Text.Value, $@"export const {Regex.Escape(name)} = `(?<value>[^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success, $"{Path} no longer exports a template literal named {name}.");
        return match.Groups["value"].Value;
    }

    private static string Number(string name)
    {
        var match = Regex.Match(Text.Value, $@"export const {Regex.Escape(name)} = (?<value>-?\d+)\s*;");

        Assert.True(match.Success, $"{Path} no longer exports a number named {name}.");
        return match.Groups["value"].Value;
    }

    private static string Quoted(string name)
    {
        var match = Regex.Match(Text.Value, $@"export const {Regex.Escape(name)} = '(?<value>[^']*)'\s*;");

        Assert.True(match.Success, $"{Path} no longer exports a string named {name}.");
        return match.Groups["value"].Value;
    }
}

/// <summary>A database file that deletes itself and its sidecars when the test
/// that made it is done.</summary>
internal sealed class TemporaryDatabase : IDisposable
{
    public TemporaryDatabase(string name = "knowledge.db")
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "backlog-knowledge-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(RootDirectory, "_meta"));
        DatabaseFile = Path.Combine(RootDirectory, "_meta", name);
    }

    /// <summary>The repository root the database sits under, so a test can hand a
    /// consumer a knowledge folder path the way the app does.</summary>
    public string RootDirectory { get; }

    /// <summary>The database itself, at the root's <c>_meta/</c>.</summary>
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
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
