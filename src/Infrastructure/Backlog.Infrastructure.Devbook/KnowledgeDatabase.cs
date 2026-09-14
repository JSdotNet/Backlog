using System.Data;
using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// The read side of <c>_meta/knowledge.db</c> — the generated knowledge index
/// local ADR 0004 puts the derived layer in.
///
/// <para><b>This never writes.</b> The Node generator creates the schema and is
/// the only thing that fills it; the connection is opened
/// <see cref="SqliteOpenMode.ReadOnly"/> rather than the <c>ReadWriteCreate</c>
/// its sibling <c>SqliteTaskRepository</c> uses, and the difference is not a
/// precaution. <c>ReadWriteCreate</c> would create an empty database where none
/// exists, turning "absent" — a rung of the degradation ladder with a defined
/// answer — into "present and empty", which is the one state the ladder has no
/// rung for and the one a panel cannot tell from a corpus that really is empty.</para>
///
/// <para><b>Open, ask, dispose.</b> Instances are short-lived on purpose. The
/// generator builds into a temporary file and renames it over this one, and on
/// Windows a rename over a file somebody holds open fails — so a reader that kept
/// a connection alive for the life of a panel would break the writer rather than
/// merely lag it. Holding one for the span of a single question costs an open and
/// buys the property that a refresh can always land.</para>
///
/// <para><b>Absence is ordinary.</b> A fresh clone has no database until somebody
/// runs the generator, a folder configured off the clone has no repository root
/// above it, and a rebuild in flight may hand back a file that will not open. All
/// three answer <see langword="null"/> from <see cref="TryOpen"/>, and every
/// consumer's response is the same: read the Markdown, which is what it did before
/// an index existed. The one exception is retrieval — see
/// <see cref="KnowledgeRetrievalTier"/>.</para>
///
/// <para><b>Unrecognised is the same as absent.</b> A <c>schemaVersion</c> this
/// reader does not know means the file is not the shape it understands, and
/// guessing at it would be worse than the scan it replaces. That is what makes the
/// version a safe blunt instrument on the writing side: bumping it switches every
/// older reader back to Markdown rather than breaking it.</para>
/// </summary>
/// <remarks>
/// Partial only so the retrieval reads — full text and vectors — sit in
/// <c>KnowledgeDatabase.Retrieval.cs</c> rather than lengthening this file. They
/// are the same class deliberately: they answer from the same connection, on the
/// same open-ask-dispose terms, and splitting them into a second type would have
/// meant a second way to open the file.
/// </remarks>
public sealed partial class KnowledgeDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IReadOnlyDictionary<string, string> _meta;

    private KnowledgeDatabase(string databasePath, SqliteConnection connection, IReadOnlyDictionary<string, string> meta)
    {
        DatabasePath = databasePath;
        _connection = connection;
        _meta = meta;
    }

    /// <summary>The file this instance is reading.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Opens the database for the repository a knowledge folder belongs to, or
    /// <see langword="null"/> when there is none to open — see the class remarks
    /// for the four ways that happens and why they are one answer.
    /// </summary>
    public static KnowledgeDatabase? TryOpenForFolder(string? knowledgeFolderPath) =>
        TryOpen(KnowledgeDatabaseLocation.ForKnowledgeFolder(knowledgeFolderPath));

    /// <summary>
    /// Opens the database at <paramref name="databasePath"/>, or
    /// <see langword="null"/> when it is absent, locked, unreadable, or written to
    /// a <c>schemaVersion</c> this reader does not recognise.
    /// </summary>
    public static KnowledgeDatabase? TryOpen(string? databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) return null;

        // Asked before opening rather than left to the connection: SQLite in
        // read-only mode reports a missing file as an error, and an error is a
        // more expensive way to learn something a stat already answers.
        if (!File.Exists(databasePath)) return null;

        SqliteConnection? connection = null;

        try
        {
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());

            connection.Open();

            var meta = ReadMeta(connection);

            if (!meta.TryGetValue(KnowledgeDatabaseSchema.SchemaVersionKey, out var declared)
                || !int.TryParse(declared, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                || version != KnowledgeDatabaseSchema.Version)
            {
                connection.Dispose();
                return null;
            }

            return new KnowledgeDatabase(databasePath, connection, meta);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Locked by a rebuild landing underneath, held by something else, or
            // not a database at all. None of them is worth failing a panel over.
            connection?.Dispose();
            return null;
        }
    }

    /// <summary>What the generator recorded about itself: the version, the
    /// timestamp, what wrote it, the scopes it covers.</summary>
    public IReadOnlyDictionary<string, string> Meta => _meta;

    /// <summary>
    /// When the generator wrote this file, or <see cref="DateTime.MinValue"/> when
    /// it recorded nothing readable.
    /// <para>
    /// The JSON artifacts carry no timestamp on purpose, so CI can diff them; a
    /// database is never diffed, so this one is free. It is the coarse answer, kept
    /// for callers that still ask "is anything newer than the index" — the exact
    /// one is <see cref="FileStates"/>.
    /// </para>
    /// </summary>
    public DateTime GeneratedUtc =>
        _meta.TryGetValue(KnowledgeDatabaseSchema.GeneratedAtKey, out var value)
        && DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : DateTime.MinValue;

    /// <summary>The embedding model the vectors in this file were produced by, or
    /// <see langword="null"/> when the semantic tier has not run. A reader ignores
    /// any vector whose model is not the one it is configured for.</summary>
    public string? EmbeddingModel =>
        _meta.TryGetValue(KnowledgeDatabaseSchema.EmbeddingModelKey, out var value) && value.Length > 0 ? value : null;

    /// <summary>Whether any chapter has been embedded. False is the ordinary
    /// state: tier 2 is optional and full-text search answers without it.</summary>
    public bool HasEmbeddings => Scalar("SELECT EXISTS (SELECT 1 FROM chapter_embedding)") != 0;

    /// <summary>Which retrieval tier this database supports.</summary>
    public KnowledgeRetrievalTier Retrieval => KnowledgeRetrieval.TierFor(this);

    /// <summary>
    /// The resolved outline of one scope — <c>.domain</c>, or
    /// <see cref="KnowledgeDatabaseSchema.RepositoryScope"/> for the whole
    /// repository — flat, in the order the writer applied, parents before
    /// children. The authored reading order is already in it; a caller rebuilds
    /// the tree from <see cref="KnowledgeOutlineRow.ParentId"/> and never sorts.
    /// </summary>
    public IReadOnlyList<KnowledgeOutlineRow> Outline(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return [];

        return Query(
            """
            SELECT id, scope, parent_id, ordinal, type, name, path, title, status, kind, is_root
            FROM outline_entry
            WHERE scope = $scope
            ORDER BY id
            """,
            command => command.Parameters.AddWithValue("$scope", scope),
            reader => new KnowledgeOutlineRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Text(reader, 7),
                Text(reader, 8),
                Text(reader, 9),
                reader.GetInt32(10) != 0));
    }

    /// <summary>
    /// One row per Markdown file inside <paramref name="scope"/>, carrying the
    /// size, modification time and hash the drift check compares against. The
    /// facts are recorded on every chapter of a file; this collapses them back to
    /// the file they describe, which is the grain the check works at.
    /// </summary>
    public IReadOnlyDictionary<string, KnowledgeFileState> FileStates(string scope)
    {
        var rows = Query(
            ScopeFilter("SELECT path, size, mtime, source_hash FROM chapter", scope, "path"),
            command => AddScope(command, scope),
            reader => new KnowledgeFileState(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3)));

        var states = new Dictionary<string, KnowledgeFileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) states[row.Path] = row;

        return states;
    }

    /// <summary>Every chapter of one file, in the order they appear in it.</summary>
    public IReadOnlyList<KnowledgeChapterRow> Chapters(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return [];

        return Query(
            """
            SELECT path, folder, slug, level, title, status, line, text, content_hash, source_hash, size, mtime
            FROM chapter
            WHERE path = $path
            ORDER BY line
            """,
            command => command.Parameters.AddWithValue("$path", path),
            reader => new KnowledgeChapterRow(
                reader.GetString(0),
                Text(reader, 1),
                reader.GetString(2),
                reader.GetInt32(3),
                Text(reader, 4),
                Text(reader, 5),
                reader.GetInt32(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetInt64(10),
                reader.GetInt64(11)));
    }

    /// <summary>Every graph node, unprojected — the whole repository in one table,
    /// because a scope is a filter rather than a file. See
    /// <see cref="KnowledgeScopeProjection"/> for the scoped reading.</summary>
    public IReadOnlyList<KnowledgeNodeRow> Nodes() =>
        Query(
            """
            SELECT id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue
            FROM node
            ORDER BY rowid
            """,
            _ => { },
            reader => new KnowledgeNodeRow(
                reader.GetString(0),
                reader.GetString(1),
                Text(reader, 2),
                Text(reader, 3),
                Text(reader, 4),
                Text(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Text(reader, 8),
                reader.GetInt32(9) != 0,
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Text(reader, 11),
                Text(reader, 12),
                Text(reader, 13)));

    /// <summary>Every graph edge, unprojected.</summary>
    public IReadOnlyList<KnowledgeEdgeRow> Edges() =>
        Query(
            "SELECT id, type, source, target FROM edge ORDER BY rowid",
            _ => { },
            reader => new KnowledgeEdgeRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));

    /// <summary>
    /// Every value of one list-valued <c>meta</c> field, by node — <c>roadmap</c>,
    /// <c>feature-flag</c>, <c>aliases</c>, <c>alternatives</c>. Asked one name at
    /// a time because a caller wants one: the roadmap rollup has no use for a
    /// chapter's aliases.
    /// </summary>
    public ILookup<string, string> Attributes(string name)
    {
        var rows = string.IsNullOrWhiteSpace(name)
            ? []
            : Query(
                "SELECT node_id, value FROM node_attribute WHERE name = $name ORDER BY rowid",
                command => command.Parameters.AddWithValue("$name", name),
                reader => (Node: reader.GetString(0), Value: reader.GetString(1)));

        return rows.ToLookup(row => row.Node, row => row.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// The diagram artifacts filed against the chapters in one directory — the
    /// rows that used to be that directory's <c>_archify/index.json</c>.
    /// <para>
    /// A directory rather than a single chapter, because the lookup needs its
    /// neighbours: an artifact filed under a different hash for the same chapter
    /// and ordinal is a picture of a fence somebody has since edited, and saying so
    /// means seeing the whole folder.
    /// </para>
    /// </summary>
    /// <param name="directory">Repository-relative, <c>/</c>-separated — the
    /// directory the chapters are in, not the <c>_archify</c> folder beside
    /// them.</param>
    public IReadOnlyList<KnowledgeArchifyArtifactRow> ArchifyArtifacts(string directory)
    {
        if (directory is null) return [];

        var trimmed = directory.Replace('\\', '/').Trim('/');
        var prefix = trimmed.Length == 0 ? string.Empty : trimmed + "/";

        var rows = Query(
            """
            SELECT chapter_path, fence_hash, ordinal, type, quality, kind, spec_path, artifact_path, checks_passed, check_count
            FROM archify_artifact
            WHERE chapter_path LIKE $prefix ESCAPE '\'
            ORDER BY rowid
            """,
            command => command.Parameters.AddWithValue("$prefix", Escape(prefix) + "%"),
            reader => new KnowledgeArchifyArtifactRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                Text(reader, 3),
                Text(reader, 4),
                Text(reader, 5),
                Text(reader, 6),
                Text(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9)));

        // A LIKE prefix matches every descendant, and `.arc42/adr/` has an
        // `_archify` folder of its own. The index sits beside the chapters, so only
        // chapters in this directory itself belong to it.
        return rows
            .Where(row => string.Equals(DirectoryOf(row.ChapterPath), trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>What the generator could not resolve, for one scope or for all of
    /// them. Recorded rather than thrown, so a panel can report a broken reference
    /// without the build having refused to produce anything.</summary>
    public IReadOnlyList<KnowledgeProblemRow> Problems(string? scope = null) =>
        Query(
            scope is null
                ? "SELECT scope, severity, path, message FROM problem ORDER BY rowid"
                : "SELECT scope, severity, path, message FROM problem WHERE scope = $scope ORDER BY rowid",
            command =>
            {
                if (scope is not null) command.Parameters.AddWithValue("$scope", scope);
            },
            reader => new KnowledgeProblemRow(reader.GetString(0), reader.GetString(1), Text(reader, 2), reader.GetString(3)));

    public void Dispose() => _connection.Dispose();

    /// <summary>The directory part of a repository-relative path, or the empty
    /// string when it is at the root.</summary>
    private static string DirectoryOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    /// <summary>A scope becomes a path filter, the same way the generator's own
    /// <c>projectScope</c> decides membership: the folder itself, or anything
    /// beneath it. The repository scope filters nothing.</summary>
    private static string ScopeFilter(string sql, string scope, string column) =>
        IsRepositoryScope(scope)
            ? sql
            : sql + " WHERE " + column + " = $scope OR " + column + " LIKE $prefix ESCAPE '\\'";

    private static void AddScope(SqliteCommand command, string scope)
    {
        if (IsRepositoryScope(scope)) return;

        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$prefix", Escape(scope + "/") + "%");
    }

    private static bool IsRepositoryScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) || scope == KnowledgeDatabaseSchema.RepositoryScope;

    /// <summary>Neutralises the wildcards in a path used as a LIKE prefix. No
    /// knowledge path carries one today, which is exactly why the day one does
    /// would go unnoticed.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? Text(IDataRecord reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static IReadOnlyDictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            meta[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return meta;
    }

    private long Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar() is long value ? value : 0;
    }

    private List<T> Query<T>(string sql, Action<SqliteCommand> bind, Func<SqliteDataReader, T> read)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        bind(command);

        var rows = new List<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(read(reader));

        return rows;
    }
}
