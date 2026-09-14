namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// The half of the knowledge database's contract this side is allowed to state:
/// the version it understands, where the file is, and the names of the tables it
/// reads.
///
/// <para>The DDL itself is deliberately absent. It lives in
/// <c>tools/knowledge/knowledge-schema.mjs</c> as one exported string, because
/// ADR 0004's named risk is a schema written in Node and read in C# drifting
/// silently — and a schema restated in two languages is that drift with extra
/// steps. What this file holds is the smallest set of facts a reader cannot avoid
/// knowing, and every one of them is pinned against that file by
/// <c>KnowledgeSchemaContractTests</c>.</para>
///
/// <para><see cref="Version"/> is the whole compatibility story. The writer bumps
/// it for any change a reader could notice; a reader that does not recognise the
/// number ignores the database entirely and reads Markdown, so a database written
/// by newer tooling can never break an older app. There is no migration
/// machinery here for the same reason there is none there: the database is a
/// build output, and the answer to a schema change is to rebuild it.</para>
/// </summary>
public static class KnowledgeDatabaseSchema
{
    /// <summary>The one <c>schemaVersion</c> this reader understands, and the
    /// value <c>SCHEMA_VERSION</c> carries in <c>knowledge-schema.mjs</c>.
    /// <para>2 added <c>chapter.search_text</c> and moved <c>chapter_fts</c> onto
    /// it, so the index holds each chapter's prose instead of its raw Markdown.
    /// A database still at 1 is ignored rather than read with fenced metadata in
    /// its excerpts — which is the bump doing its job, and the Markdown path
    /// answering until <c>build-database.mjs</c> runs again.</para></summary>
    public const int Version = 2;

    /// <summary>The database's location, relative to the repository root — the
    /// same path <c>DATABASE_PATH</c> names on the writing side. It is
    /// <c>_meta/</c> because the derived-artifacts convention puts a cross-cutting
    /// generated artifact one level below the thing it describes, and the
    /// repository root's <c>_meta/</c> is exactly that place.</summary>
    public const string RelativePath = "_meta/knowledge.db";

    /// <summary>The generator's <c>meta</c> key for the version.</summary>
    public const string SchemaVersionKey = "schemaVersion";

    /// <summary>The generator's <c>meta</c> key for when the file was written.
    /// The JSON artifacts carry no timestamp so CI can diff them; the database is
    /// never diffed, so a timestamp is free and it is what a reader with no
    /// per-file hash falls back to.</summary>
    public const string GeneratedAtKey = "generatedAt";

    /// <summary>The generator's <c>meta</c> key naming what wrote the file.</summary>
    public const string GeneratedByKey = "generatedBy";

    /// <summary>The generator's <c>meta</c> key for the scopes the file covers.</summary>
    public const string ScopesKey = "scopes";

    /// <summary>The <c>meta</c> key recording which model produced the vectors in
    /// <c>chapter_embedding</c>. Absent until the semantic tier has run; a reader
    /// ignores any vector whose model is not the one it is configured for.</summary>
    public const string EmbeddingModelKey = "embeddingModel";

    /// <summary>The repository-wide scope, as <c>outline_entry.scope</c> spells
    /// it — <c>REPO_SCOPE</c> on the writing side.</summary>
    public const string RepositoryScope = ".";

    /// <summary>
    /// Every table this reader reads from, named so a contract test can assert
    /// the writer still creates each one and that a row put in each comes back
    /// out.
    /// <para>
    /// A list of what is read, not an inventory of what exists.
    /// <c>chapter_fts</c> is deliberately absent: it is created and filled by the
    /// writer, and nothing here queries it until retrieval arrives. That it exists
    /// and that FTS5 is compiled into the shipped SQLite are pinned separately, by
    /// <c>KnowledgeSchemaContractTests</c>, so the retrieval tier fails at test
    /// time rather than silently at runtime if the package ever changes.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ReadTables { get; } =
    [
        "meta",
        "node",
        "node_attribute",
        "edge",
        "outline_entry",
        "chapter",
        "chapter_embedding",
        "archify_artifact",
        "problem"
    ];
}
