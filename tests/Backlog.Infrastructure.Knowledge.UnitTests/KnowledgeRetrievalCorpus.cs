using System.Buffers.Binary;

using Backlog.Modules.Knowledge.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// A corpus with words worth searching for and vectors worth ranking, built
/// through the writer's own DDL.
///
/// <para>Separate from <see cref="KnowledgeCorpus"/> rather than an extension of
/// it, because the two are answering different questions. That one is "does every
/// column round-trip", so it holds one of everything; this one is "does retrieval
/// rank the right chapter above the wrong one", which needs several chapters that
/// differ in the ways the ranking is supposed to notice.</para>
/// </summary>
internal static class KnowledgeRetrievalCorpus
{
    public const string DomainScope = ".domain";

    public const string ArchitectureScope = ".arc42";

    /// <summary>The chapter whose own heading is the phrase, so the title
    /// weighting has something to prefer.</summary>
    public const string LadderPath = ".arc42/adr/0004-ladder.md";

    /// <summary>A chapter that mentions the phrase in passing.</summary>
    public const string MentionPath = ".domain/second-brain/features.md";

    /// <summary>A chapter about something else entirely.</summary>
    public const string UnrelatedPath = ".domain/capture/domain.md";

    /// <summary>A chapter whose Markdown opens with a <c>meta</c> fence, as every
    /// authored chapter in this repository does. Its <c>search_text</c> is what
    /// the writer would index for it — the fence gone — which is the only reason
    /// an excerpt taken from it reads as prose.</summary>
    public const string FencedPath = ".domain/second-brain/naming.md";

    public const string Model = "text-embedding-3-small";

    /// <summary>Creates the database and fills <c>chapter</c> and the external-content
    /// <c>chapter_fts</c> over it. Nothing else: retrieval reads those two tables and
    /// a fixture that filled the rest would be describing a different test.</summary>
    public static void Seed(string databasePath)
    {
        KnowledgeSchemaSource.Create(databasePath, KnowledgeDatabaseSchema.Version);

        using var connection = KnowledgeSchemaSource.OpenForWriting(databasePath);

        Chapter(connection, LadderPath, "arc42", "the-degradation-ladder", "The degradation ladder", 12,
            "Each rung is a defined state with defined behaviour, and only the last is visible.");
        Chapter(connection, MentionPath, "domain", "knowledge-retrieval", "Knowledge retrieval", 321,
            "Results name the chapter they came from rather than returning loose text. A ladder of fallbacks sits under it.");
        Chapter(connection, UnrelatedPath, "domain", "capture-source", "Capture source", 7,
            "Quick capture never blocks on a decision.");

        // The two columns differ here, which is the whole point of this row: the
        // Markdown carries a `meta` fence and the indexed text does not.
        Chapter(connection, FencedPath, "domain", "knowledge-note", "Knowledge Note", 21,
            "## Knowledge Note\n\n```meta\ntype: aggregate\nstatus: draft\naliases: [KnowledgeNote, Note]\n```\n\n"
            + "The durable unit of captured knowledge.",
            "Knowledge Note\n\nThe durable unit of captured knowledge.");

        // External-content FTS5 is filled explicitly after `chapter`, exactly as
        // the writer does it - there are no synchronisation triggers, because
        // nothing ever updates a row in a database that is built whole. And from
        // `search_text`, exactly as the writer does that too: `snippet()` returns
        // the indexed column, so indexing the raw Markdown is what put fences in
        // front of a reader.
        KnowledgeSchemaSource.Execute(
            connection,
            "INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter");

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    /// <summary>Records a vector against a chapter, as the semantic tier would.
    /// <paramref name="model"/> is a parameter because "a vector some other model
    /// produced" is the case the reader has to ignore.</summary>
    public static void Embed(string databasePath, string chapterPath, string model, params float[] vector)
    {
        using var connection = KnowledgeSchemaSource.OpenForWriting(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO chapter_embedding (content_hash, model, dimensions, vector)
            SELECT content_hash, $model, $dimensions, $vector FROM chapter WHERE path = $path
            """;
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$dimensions", vector.Length);
        command.Parameters.AddWithValue("$vector", Encode(vector));
        command.Parameters.AddWithValue("$path", chapterPath);

        Assert.Equal(1, command.ExecuteNonQuery());

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    /// <summary>Writes a blob that is deliberately not <paramref name="dimensions"/>
    /// floats long, which is the shape a truncated or half-written row has.</summary>
    public static void EmbedMalformed(string databasePath, string chapterPath, string model, int dimensions, byte[] bytes)
    {
        using var connection = KnowledgeSchemaSource.OpenForWriting(databasePath);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO chapter_embedding (content_hash, model, dimensions, vector)
            SELECT content_hash, $model, $dimensions, $vector FROM chapter WHERE path = $path
            """;
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$dimensions", dimensions);
        command.Parameters.AddWithValue("$vector", bytes);
        command.Parameters.AddWithValue("$path", chapterPath);

        Assert.Equal(1, command.ExecuteNonQuery());

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    /// <summary>
    /// The wire format: little-endian IEEE-754 singles, packed, no header.
    ///
    /// <para>Written here rather than taken from the reader, so the round-trip
    /// asserts an agreement instead of asserting that one implementation is
    /// self-consistent. This is the same pairing <c>normalizeDiagramSource</c> and
    /// <c>DiagramSourceHash</c> have, and it is why the layout is written down in
    /// <c>tools/knowledge/knowledge-schema.mjs</c> beside the table.</para>
    /// </summary>
    public static byte[] Encode(params float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];

        for (var index = 0; index < vector.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), vector[index]);
        }

        return bytes;
    }

    private static void Chapter(
        SqliteConnection connection,
        string path,
        string folder,
        string slug,
        string title,
        int line,
        string text,
        string? searchText = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
            VALUES ($path, $folder, $slug, 2, $title, 'active', $line, $text, $searchText, $contentHash, $sourceHash, $size, 0)
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$folder", folder);
        command.Parameters.AddWithValue("$slug", slug);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$line", line);
        command.Parameters.AddWithValue("$text", text);
        // A chapter with no fences indexes as itself, which is what the writer
        // produces for one; the default keeps that case from stating it twice.
        command.Parameters.AddWithValue("$searchText", searchText ?? text);
        command.Parameters.AddWithValue("$contentHash", KnowledgeCorpus.Sha256(path + "#" + slug));
        command.Parameters.AddWithValue("$sourceHash", KnowledgeCorpus.Sha256(path));
        command.Parameters.AddWithValue("$size", text.Length);

        command.ExecuteNonQuery();
    }
}

/// <summary>
/// The knowledge-folder port, answering for one directory on disk.
///
/// <para>The adapters under test take this port rather than a path, because that
/// is how every knowledge consumer is addressed: a repository alias resolves to a
/// set of folders, and the database sits at the root above them. So the fake has
/// to answer the same shape — including the case that matters here, a folder that
/// resolves to somewhere with no database above it.</para>
/// </summary>
internal sealed class StubKnowledgeFolderSource(string? rootDirectory, string folderKey = ".domain") : IKnowledgeFolderSource
{
    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <summary>Nothing subscribes in these tests, so nothing is published.</summary>
    public void NotifyContentChanged()
    {
    }

    public string StorageDirectory => rootDirectory ?? string.Empty;

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) =>
        [new KnowledgeFolderSetting(folderKey, "Domain", folderKey)];

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null) =>
        rootDirectory is null
            ? KnowledgeFolderLocation.Unavailable(key, "No folder is configured.")
            : new KnowledgeFolderLocation(
                key,
                Available: true,
                Message: null,
                RepositoryFullName: null,
                Folder: new KnowledgeFolderSetting(key, "Domain", key),
                FullPath: Path.Combine(rootDirectory, key));
}
