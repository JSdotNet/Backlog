using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// The cross-language contract ADR 0004 names as this change's main risk: the
/// schema is written by <c>tools/knowledge/build-database.mjs</c> and read from
/// C#, so it can drift silently in a way neither side can detect from the inside.
///
/// <para>The treatment is the one the Archify hash rule already gets — pin it from
/// both sides, and have the two tests name each other.
/// <c>DiagramSourceHash.Normalize</c> and <c>normalizeDiagramSource</c> are that
/// pairing; this is the same idea applied to a schema instead of a hash, and
/// <c>tools/knowledge/build-database.test.mjs</c> is its other half.</para>
///
/// <para>Every database below is created from the DDL read out of
/// <c>knowledge-schema.mjs</c> at test time, never from a copy in C#. That is the
/// property worth protecting: the reading side states a version, a path and a list
/// of table names, and nothing else about the shape.</para>
/// </summary>
public class KnowledgeSchemaContractTests
{
    [Fact]
    public void The_schema_version_matches_the_writers()
    {
        Assert.Equal(KnowledgeDatabaseSchema.Version, KnowledgeSchemaSource.Version);
    }

    [Fact]
    public void The_database_path_matches_the_writers()
    {
        Assert.Equal(KnowledgeDatabaseSchema.RelativePath, KnowledgeSchemaSource.DatabasePath);
    }

    [Fact]
    public void The_writer_creates_every_table_the_reader_reads()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile, KnowledgeDatabaseSchema.Version);

        using var connection = KnowledgeSchemaSource.OpenForWriting(temporary.DatabaseFile);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table', 'view')";

        var tables = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        foreach (var table in KnowledgeDatabaseSchema.ReadTables)
        {
            Assert.True(
                tables.Contains(table, StringComparer.Ordinal),
                $"{KnowledgeSchemaSource.Path} no longer creates '{table}', which KnowledgeDatabase reads.");
        }
    }

    /// <summary>
    /// A row in every table the reader claims to read, put in through the writer's
    /// own DDL and taken back out through the reader's own accessors. The point is
    /// the column names and their order: a rename on the writing side fails here
    /// rather than emptying a panel.
    /// </summary>
    [Fact]
    public void Every_table_the_reader_claims_round_trips_a_row()
    {
        using var temporary = new TemporaryDatabase();
        var seeded = KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        // meta
        Assert.Equal(KnowledgeDatabaseSchema.Version.ToString(), database.Meta[KnowledgeDatabaseSchema.SchemaVersionKey]);
        Assert.Equal(seeded.GeneratedAt, database.GeneratedUtc);
        Assert.Equal(seeded.EmbeddingModel, database.EmbeddingModel);

        // node
        var node = Assert.Single(database.Nodes(), candidate => candidate.Id == KnowledgeCorpus.ChapterId);
        Assert.Equal("chapter", node.Type);
        Assert.Equal("Inbox", node.Label);
        Assert.Equal("domain", node.Folder);
        Assert.Equal(KnowledgeCorpus.ChapterPath, node.Path);
        Assert.Equal("inbox", node.Slug);
        Assert.Equal(2, node.Level);
        Assert.Equal(7, node.Line);
        Assert.Equal("active", node.Status);
        Assert.False(node.OutOfScope);
        Assert.Equal(5, node.Effort);
        Assert.Equal("aggregate", node.Kind);
        Assert.Equal("1.2.0", node.Version);
        Assert.Equal("241", node.Issue);

        // node_attribute
        Assert.Equal(["inbox-triage"], database.Attributes("roadmap")[KnowledgeCorpus.ChapterId]);

        // edge
        var edge = Assert.Single(database.Edges(), candidate => candidate.Type == "related");
        Assert.Equal(KnowledgeCorpus.ChapterId, edge.Source);
        Assert.Equal(KnowledgeCorpus.ExternalId, edge.Target);

        // outline_entry
        var outline = database.Outline(KnowledgeCorpus.Scope);
        var directory = Assert.Single(outline, entry => entry.Type == "directory");
        var file = Assert.Single(outline, entry => entry.Type == "file");
        Assert.Null(directory.ParentId);
        Assert.Equal(directory.Id, file.ParentId);
        Assert.Equal(0, file.Ordinal);
        Assert.Equal("domain.md", file.Name);
        Assert.Equal(KnowledgeCorpus.ChapterPath, file.Path);
        Assert.Equal("Inbox", file.Title);
        Assert.Equal("active", file.Status);
        Assert.Equal("domain", file.Kind);
        Assert.True(file.IsRoot);

        // chapter
        var chapter = Assert.Single(database.Chapters(KnowledgeCorpus.ChapterPath));
        Assert.Equal("domain", chapter.Folder);
        Assert.Equal("inbox", chapter.Slug);
        Assert.Equal(2, chapter.Level);
        Assert.Equal("Inbox", chapter.Title);
        Assert.Equal("active", chapter.Status);
        Assert.Equal(7, chapter.Line);
        Assert.Equal(KnowledgeCorpus.ChapterText, chapter.Text);
        Assert.Equal(KnowledgeCorpus.ContentHash, chapter.ContentHash);
        Assert.Equal(seeded.SourceHash, chapter.SourceHash);
        Assert.Equal(seeded.Size, chapter.Size);
        Assert.Equal(seeded.Mtime, chapter.Mtime);

        // chapter_embedding
        Assert.True(database.HasEmbeddings);

        // archify_artifact
        var artifact = Assert.Single(database.ArchifyArtifacts(".domain/inbox"));
        Assert.Equal(KnowledgeCorpus.FenceHash, artifact.FenceHash);
        Assert.Equal(3, artifact.Ordinal);
        Assert.Equal("architecture", artifact.Type);
        Assert.Equal("showcase", artifact.Quality);
        Assert.Equal("flowchart", artifact.Kind);
        Assert.Equal(".domain/inbox/_archify/domain.3.architecture.json", artifact.SpecPath);
        Assert.Equal(".domain/inbox/_archify/domain.3.architecture.html", artifact.ArtifactPath);
        Assert.Equal(9, artifact.ChecksPassed);
        Assert.Equal(9, artifact.CheckCount);

        // problem
        var problem = Assert.Single(database.Problems(KnowledgeCorpus.Scope));
        Assert.Equal("warning", problem.Severity);
        Assert.Equal(KnowledgeCorpus.ChapterPath, problem.Path);
        Assert.Equal("Unresolved reference.", problem.Message);
    }

    /// <summary>
    /// FTS5 is compiled into the SQLite that <c>Microsoft.Data.Sqlite</c> pulls in
    /// transitively through <c>SQLitePCLRaw.bundle_e_sqlite3</c>. The whole
    /// retrieval tier rests on that, and nothing in a build or a restore would say
    /// if a package change took it away — the schema would simply fail to apply on
    /// the reading side while the Node writer went on producing the table. Asked
    /// here so it fails loudly at test time instead.
    /// </summary>
    [Fact]
    public void Fts5_is_available_in_the_shipped_sqlite_build()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile, KnowledgeDatabaseSchema.Version);

        using var connection = KnowledgeSchemaSource.OpenForWriting(temporary.DatabaseFile);

        KnowledgeSchemaSource.Execute(
            connection,
            """
            INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
            VALUES ('.domain/inbox/domain.md', 'domain', 'inbox', 2, 'Inbox', 'active', 7,
                    '## Inbox' || char(10) || 'Quick capture never blocks on a decision.',
                    'Inbox' || char(10) || 'Quick capture never blocks on a decision.', 'aa', 'bb', 41, 0)
            """);

        KnowledgeSchemaSource.Execute(
            connection,
            "INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM chapter_fts WHERE chapter_fts MATCH '\"quick capture\"'";

        Assert.Equal(1L, Assert.IsType<long>(command.ExecuteScalar()));
    }

    /// <summary>
    /// The writer sets WAL and closes, which checkpoints and removes the sidecars.
    /// A read-only connection then has to open a database whose header still says
    /// WAL — a combination that is easy to assume works and expensive to discover
    /// does not, because the symptom would be every panel silently on the Markdown
    /// path.
    /// </summary>
    [Fact]
    public void A_read_only_connection_opens_a_database_the_writer_left_in_wal_mode()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile, KnowledgeDatabaseSchema.Version);

        Assert.False(File.Exists(temporary.DatabaseFile + "-wal"), "The writer is expected to leave no write-ahead log behind.");

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);

        Assert.NotNull(database);
    }
}
