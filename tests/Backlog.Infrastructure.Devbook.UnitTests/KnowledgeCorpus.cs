using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// One row in every table, filled through the writer's own DDL.
///
/// <para>Small on purpose. What these tests are about is the shape of the
/// contract, not the size of the corpus, and a fixture with one of everything
/// makes an assertion that names a column fail with the column in the message.</para>
/// </summary>
internal static class KnowledgeCorpus
{
    public const string Scope = ".domain";

    public const string ChapterPath = ".domain/inbox/domain.md";

    public const string ChapterId = ".domain/inbox/domain.md#inbox";

    /// <summary>A reference target outside the knowledge folders: no folder, no
    /// path beneath the scope, and therefore a boundary node in every projection
    /// that reaches it.</summary>
    public const string ExternalId = "https://learn.microsoft.com/dotnet";

    public const string ChapterText = "## Inbox\n\nQuick capture never blocks on a decision.\n";

    /// <summary>The same chapter as the writer indexes it: prose, with the
    /// heading's marker off the front. <c>chapter.text</c> and
    /// <c>chapter.search_text</c> are two columns because they differ, so a
    /// fixture that filled both from one string would be describing a corpus the
    /// generator never writes.</summary>
    public const string ChapterSearchText = "Inbox\n\nQuick capture never blocks on a decision.";

    public const string FenceHash = "5f2b7c1d9e0a4b6c8d3e1f0a2b4c6d8e0f1a3b5c7d9e1f3a5b7c9d1e3f5a7b9c";

    /// <summary>The hash of <see cref="ChapterText"/>, computed the way the writer
    /// computes it, so the round-trip asserts a real value rather than a token.</summary>
    public static string ContentHash { get; } = Sha256(ChapterText);

    /// <summary>What the fixture recorded, for the assertions that compare against
    /// it rather than against a literal.</summary>
    internal sealed record Seeded(DateTime GeneratedAt, string EmbeddingModel, string SourceHash, long Size, long Mtime);

    /// <summary>
    /// Creates the database and fills every table the reader claims to read.
    /// Also writes the Markdown file behind the chapter, under
    /// <paramref name="databasePath"/>'s repository root, so the drift check has
    /// something real to stat.
    /// </summary>
    public static Seeded Seed(string databasePath, string? markdown = null)
    {
        KnowledgeSchemaSource.Create(databasePath, KnowledgeDatabaseSchema.Version);

        var source = markdown ?? "# Second Brain\n\n" + ChapterText;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(databasePath))!;
        var file = Path.Combine(root, ChapterPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, source);

        var info = new FileInfo(file);
        var seeded = new Seeded(
            new DateTime(2026, 9, 8, 1, 23, 45, DateTimeKind.Utc),
            "text-embedding-3-small",
            Sha256(File.ReadAllBytes(file)),
            info.Length,
            KnowledgeFileState.UnixMilliseconds(info.LastWriteTimeUtc));

        using var connection = KnowledgeSchemaSource.OpenForWriting(databasePath);

        Fill(connection, seeded);

        SqliteConnection.ClearPool(connection);
        connection.Close();

        return seeded;
    }

    private static void Fill(SqliteConnection connection, Seeded seeded)
    {
        Run(
            connection,
            "INSERT INTO meta (key, value) VALUES ('generatedBy', 'tools/knowledge/build-database.mjs')");
        Run(
            connection,
            "INSERT INTO meta (key, value) VALUES ('generatedAt', $generatedAt)",
            ("$generatedAt", seeded.GeneratedAt.ToString("O", CultureInfo.InvariantCulture)));
        Run(connection, "INSERT INTO meta (key, value) VALUES ('scopes', '.,.domain')");
        Run(
            connection,
            "INSERT INTO meta (key, value) VALUES ('embeddingModel', $model)",
            ("$model", seeded.EmbeddingModel));

        Run(
            connection,
            """
            INSERT INTO node (id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue)
            VALUES ($id, 'chapter', 'Inbox', 'domain', $path, 'inbox', 2, 7, 'active', 0, 5, 'aggregate', '1.2.0', '241')
            """,
            ("$id", ChapterId),
            ("$path", ChapterPath));

        Run(
            connection,
            """
            INSERT INTO node (id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue)
            VALUES ($id, 'external', 'Microsoft Learn', NULL, NULL, NULL, NULL, NULL, NULL, 1, NULL, NULL, NULL, NULL)
            """,
            ("$id", ExternalId));

        Run(
            connection,
            "INSERT INTO node_attribute (node_id, name, value) VALUES ($id, 'roadmap', 'inbox-triage')",
            ("$id", ChapterId));

        Run(
            connection,
            "INSERT INTO edge (id, type, source, target) VALUES ($id, 'related', $source, $target)",
            ("$id", ChapterId + "->" + ExternalId + ":related"),
            ("$source", ChapterId),
            ("$target", ExternalId));

        Run(
            connection,
            """
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ($scope, NULL, 0, 'directory', 'inbox', '.domain/inbox', 'Inbox', NULL, 'domain', 0)
            """,
            ("$scope", Scope));

        Run(
            connection,
            """
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            SELECT $scope, id, 0, 'file', 'domain.md', $path, 'Inbox', 'active', 'domain', 1
            FROM outline_entry WHERE type = 'directory'
            """,
            ("$scope", Scope),
            ("$path", ChapterPath));

        Run(
            connection,
            """
            INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
            VALUES ($path, 'domain', 'inbox', 2, 'Inbox', 'active', 7, $text, $searchText, $contentHash, $sourceHash, $size, $mtime)
            """,
            ("$path", ChapterPath),
            ("$text", ChapterText),
            ("$searchText", ChapterSearchText),
            ("$contentHash", ContentHash),
            ("$sourceHash", seeded.SourceHash),
            ("$size", seeded.Size),
            ("$mtime", seeded.Mtime));

        Run(connection, "INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter");

        Run(
            connection,
            "INSERT INTO chapter_embedding (content_hash, model, dimensions, vector) VALUES ($hash, $model, 3, $vector)",
            ("$hash", ContentHash),
            ("$model", seeded.EmbeddingModel),
            ("$vector", new byte[] { 1, 2, 3 }));

        Run(
            connection,
            """
            INSERT INTO archify_artifact (chapter_path, fence_hash, ordinal, type, quality, kind, spec_path, artifact_path, checks_passed, check_count)
            VALUES ($path, $hash, 3, 'architecture', 'showcase', 'flowchart',
                    '.domain/inbox/_archify/domain.3.architecture.json',
                    '.domain/inbox/_archify/domain.3.architecture.html', 9, 9)
            """,
            ("$path", ChapterPath),
            ("$hash", FenceHash));

        Run(
            connection,
            "INSERT INTO problem (scope, severity, path, message) VALUES ($scope, 'warning', $path, 'Unresolved reference.')",
            ("$scope", Scope),
            ("$path", ChapterPath));
    }

    private static void Run(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        command.ExecuteNonQuery();
    }

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
