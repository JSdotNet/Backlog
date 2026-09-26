namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// The devbook database's contract as this side states it: the version it
/// understands, the DDL it builds with, and the names of the tables it reads.
///
/// <para>The DDL is not restated here. It is <c>tools/devbook/devbook-schema.sql</c>,
/// embedded in this assembly, and the same file the Node writer loads — one text
/// for both writers (local ADR 0015), so a column cannot be renamed on one side
/// only. <c>DevbookSchemaContractTests</c> pins <see cref="Version"/> to that
/// file's <c>schema-version</c> line.</para>
///
/// <para><see cref="Version"/> is the whole compatibility story. A reader that does
/// not recognise the number ignores the database entirely and reads Markdown, so a
/// database written by newer tooling can never break an older app, and the app
/// rebuilds its own in the version it reads. There is no migration machinery: the
/// database is a build output, and the answer to a schema change is to rebuild
/// it.</para>
/// </summary>
public static class DevbookDatabaseSchema
{
    /// <summary>The one <c>schemaVersion</c> this reader understands and this
    /// builder writes — the <c>schema-version</c> line of <c>devbook-schema.sql</c>.
    /// <para>2 added <c>chapter.search_text</c> and moved <c>chapter_fts</c> onto
    /// it, so the index holds each chapter's prose instead of its raw
    /// Markdown.</para></summary>
    public const int Version = 2;

    /// <summary>The embedded resource holding <c>devbook-schema.sql</c>.</summary>
    internal const string DdlResourceName = "Backlog.Infrastructure.Devbook.devbook-schema.sql";

    private static readonly Lazy<string> DdlText = new(() =>
    {
        using var stream = typeof(DevbookDatabaseSchema).Assembly.GetManifestResourceStream(DdlResourceName)
            ?? throw new InvalidOperationException($"The embedded schema {DdlResourceName} is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>Every table, virtual table and index of the database, exactly as
    /// <c>tools/devbook/devbook-schema.sql</c> holds them.</summary>
    public static string Ddl => DdlText.Value;

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
    /// <c>DevbookSchemaContractTests</c>, so the retrieval tier fails at test
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
