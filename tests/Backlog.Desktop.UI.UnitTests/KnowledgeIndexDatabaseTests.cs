using System.Text.RegularExpressions;

using Backlog.Infrastructure.Knowledge;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// <c>KnowledgeIndexDocument</c> against the generated database, which is where
/// local ADR 0004 moved the reading outline.
///
/// <para>What these hold is the property the rest of Second Brain depends on: the
/// shape a caller gets back is the same whichever rung answered. The panels ask
/// for entries, titles, statuses and a staleness verdict, and none of them knows
/// whether that came out of <c>_meta/knowledge.db</c>, out of the committed
/// <c>_meta/index.json</c>, or out of a directory scan.</para>
///
/// <para>The database here is created from the DDL in
/// <c>tools/knowledge/knowledge-schema.mjs</c>, read at test time, for the same
/// reason <c>KnowledgeSchemaContractTests</c> does it: nothing on the C# side is
/// allowed to restate the schema, including a fixture.</para>
/// </summary>
public sealed class KnowledgeIndexDatabaseTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-knowledge-index-tests",
        Guid.NewGuid().ToString("N"));

    private const string Scope = ".domain";

    private const string ChapterPath = ".domain/inbox/domain.md";

    private const string SecondPath = ".domain/inbox/features.md";

    [Fact]
    public void The_outline_comes_out_of_the_database_in_the_authored_order()
    {
        var folder = Arrange();

        var index = KnowledgeIndexDocument.TryRead(folder);

        Assert.NotNull(index);

        var directory = Assert.Single(index.Directories);
        Assert.Equal("inbox", directory.Name);
        Assert.Equal("Inbox", directory.Title);

        // features.md is second in the outline and first alphabetically, so an
        // order that came out right by accident would put it first.
        Assert.Equal([ChapterPath, SecondPath], index.Files.Select(entry => entry.Path));
        Assert.Equal("Inbox", directory.RootDocument?.Title);
        Assert.Equal("active", directory.RootDocument?.Status);
    }

    [Fact]
    public void A_file_the_database_still_describes_is_not_stale()
    {
        var folder = Arrange();

        var index = KnowledgeIndexDocument.TryRead(folder);
        Assert.NotNull(index);

        Assert.All(index.Files, entry => Assert.False(index.IsStale(entry)));
        Assert.All(index.Files, entry => Assert.True(index.Exists(entry)));
    }

    [Fact]
    public void A_file_written_since_the_build_is_stale()
    {
        var folder = Arrange();

        var index = KnowledgeIndexDocument.TryRead(folder);
        Assert.NotNull(index);

        var entry = index.Files.First(candidate => candidate.Path == ChapterPath);
        File.AppendAllText(index.FullPath(entry), "\nA sentence the index never saw.\n");

        Assert.True(index.IsStale(entry));
        Assert.False(index.IsStale(index.Files.First(candidate => candidate.Path == SecondPath)));
    }

    /// <summary>
    /// The rung below the database. A repository that still has the committed
    /// artifacts and has never built a database reads exactly as it did before, and
    /// the caller cannot tell.
    /// </summary>
    [Fact]
    public void Without_a_database_the_committed_index_still_answers()
    {
        var folder = Arrange(withDatabase: false);

        Directory.CreateDirectory(Path.Combine(folder, "_meta"));
        File.WriteAllText(Path.Combine(folder, "_meta", "index.json"), """
            {
              "schemaVersion": 1,
              "entries": [
                { "type": "file", "path": ".domain/inbox/domain.md", "title": "From the JSON", "status": "draft" }
              ]
            }
            """);

        var index = KnowledgeIndexDocument.TryRead(folder);

        Assert.NotNull(index);
        Assert.Equal("From the JSON", Assert.Single(index.Files).Title);
    }

    /// <summary>
    /// A database written by a generator this reader does not know is ignored
    /// entirely rather than guessed at, and the JSON beneath it answers. That is
    /// what makes the version bump on the writing side a safe blunt instrument.
    /// </summary>
    [Fact]
    public void An_unrecognised_schema_version_falls_through_to_the_committed_index()
    {
        var folder = Arrange(schemaVersion: KnowledgeDatabaseSchema.Version + 1);

        Directory.CreateDirectory(Path.Combine(folder, "_meta"));
        File.WriteAllText(Path.Combine(folder, "_meta", "index.json"), """
            {
              "schemaVersion": 1,
              "entries": [
                { "type": "file", "path": ".domain/inbox/domain.md", "title": "From the JSON", "status": "draft" }
              ]
            }
            """);

        var index = KnowledgeIndexDocument.TryRead(folder);

        Assert.NotNull(index);
        Assert.Equal("From the JSON", Assert.Single(index.Files).Title);
    }

    [Fact]
    public void With_neither_source_there_is_no_index_and_the_caller_scans()
    {
        Assert.Null(KnowledgeIndexDocument.TryRead(Arrange(withDatabase: false)));
    }

    /// <summary>
    /// Two Markdown files under one bounded context, and — unless
    /// <paramref name="withDatabase"/> says otherwise — a database describing them,
    /// built from the writer's own DDL.
    /// </summary>
    private string Arrange(bool withDatabase = true, int? schemaVersion = null)
    {
        var folder = Path.Combine(_root, Scope);
        Directory.CreateDirectory(Path.Combine(folder, "inbox"));

        Write(ChapterPath, "# Inbox\n\nQuick capture never blocks on a decision.\n");
        Write(SecondPath, "# Features\n\nWhat the context does.\n");

        if (!withDatabase) return folder;

        var databasePath = Path.Combine(_root, "_meta", "knowledge.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, WriterSchema());
        Execute(
            connection,
            $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{schemaVersion ?? KnowledgeDatabaseSchema.Version}')");
        Execute(connection, "INSERT INTO meta (key, value) VALUES ('generatedAt', '2026-09-08T01:23:45.000Z')");

        Execute(connection, $"""
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ('{Scope}', NULL, 0, 'directory', 'inbox', '.domain/inbox', 'Inbox', NULL, 'domain', 0)
            """);

        Execute(connection, $"""
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ('{Scope}', 1, 0, 'file', 'domain.md', '{ChapterPath}', 'Inbox', 'active', 'domain', 1),
                   ('{Scope}', 1, 1, 'file', 'features.md', '{SecondPath}', 'Features', 'draft', 'domain', 0)
            """);

        foreach (var path in new[] { ChapterPath, SecondPath })
        {
            var file = new FileInfo(Resolve(path));
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.FullName)));
            var mtime = (long)Math.Round(
                (double)(new DateTimeOffset(file.LastWriteTimeUtc).UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Execute(connection, $"""
                INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
                VALUES ('{path}', 'domain', 'slug', 1, 'Title', 'active', 1, 'text', 'text', 'aa', '{hash}', {file.Length}, {mtime})
                """);
        }

        SqliteConnection.ClearPool(connection);
        connection.Close();

        return folder;
    }

    private void Write(string relativePath, string markdown) => File.WriteAllText(Resolve(relativePath), markdown);

    private string Resolve(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The writer's DDL, read out of <c>tools/knowledge/knowledge-schema.mjs</c>.
    /// Copying it into C# would make this fixture agree with a schema nobody
    /// writes, which is precisely the drift ADR 0004 asks to be pinned.
    /// </summary>
    private static string WriterSchema()
    {
        var source = File.ReadAllText(RepositoryRoot.File("tools", "knowledge", "knowledge-schema.mjs"));
        var match = Regex.Match(source, @"export const KNOWLEDGE_SCHEMA = `(?<value>[^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success, "tools/knowledge/knowledge-schema.mjs no longer exports KNOWLEDGE_SCHEMA.");
        return match.Groups["value"].Value;
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
