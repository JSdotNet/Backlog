using System.Buffers.Binary;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// The retrieval half of the reader: <c>chapter_fts</c> and
/// <c>chapter_embedding</c>.
///
/// <para>Both queries end at a chapter address, and that is not incidental.
/// <c>.domain/second-brain/features.md#knowledge-retrieval</c> asks for results
/// that "name the chapter they came from rather than returning loose text",
/// so every row below joins back to <c>chapter</c> for the path and slug before
/// it is allowed out of this class. The FTS table stores no address of its own —
/// it is external-content over <c>chapter</c>, and its <c>rowid</c> is
/// <c>chapter.id</c> — so the join is also the only way to get one.</para>
/// </summary>
public sealed partial class KnowledgeDatabase
{
    /// <summary>
    /// Weighting for <c>bm25</c>: a hit in a chapter's own heading counts for
    /// five of a hit in its body.
    /// <para>
    /// Deliberately lopsided, because on this corpus the title is the chapter's
    /// name for itself. A reader typing "degradation ladder" wants the chapter
    /// called that, not the twelve that mention it in passing, and bm25's own
    /// length normalisation does not know that a two-word field is a name rather
    /// than a very short document.
    /// </para>
    /// </summary>
    private const string Bm25 = "bm25(chapter_fts, 5.0, 1.0)";

    /// <summary>
    /// Chapters matching an FTS5 expression, best first.
    ///
    /// <para><paramref name="match"/> is FTS5 query syntax and not a reader's
    /// typing — see <see cref="KnowledgeSearchExpression"/>, which is the only
    /// thing that should be producing it. Handing user input straight to the
    /// MATCH parser is a syntax error waiting for the first apostrophe.</para>
    ///
    /// <para><c>snippet</c> takes column 1, which is <c>search_text</c>: the
    /// chapter's prose, with its <c>meta</c> and <c>annotation</c> fences and its
    /// diagrams already left out by the writer. Nothing here cleans an excerpt
    /// up, and nothing here should — an excerpt is a window onto what was
    /// indexed, so text that should not be shown is text that should not have
    /// matched either. See <c>tools/knowledge/knowledge-schema.mjs</c>.</para>
    ///
    /// <para>The match runs in a subquery so <c>snippet</c> and <c>bm25</c> are
    /// evaluated where FTS5 wants them — against the virtual table with the MATCH
    /// in scope — and the join to <c>chapter</c> then adds the address the FTS
    /// table does not carry. Scoring stays with FTS5 rather than being
    /// recomputed here for the same reason the graph is not re-derived from
    /// Markdown: there is already one answer.</para>
    /// </summary>
    /// <param name="scope">A knowledge folder, or <see langword="null"/> for
    /// every area at once.</param>
    public IReadOnlyList<KnowledgeSearchRow> SearchChapters(string? match, string? scope, int limit)
    {
        if (string.IsNullOrWhiteSpace(match) || limit <= 0) return [];

        var sql = $"""
            SELECT chapter.path, chapter.folder, chapter.slug, chapter.title, matched.excerpt, matched.score
            FROM (
                SELECT rowid AS id,
                       snippet(chapter_fts, 1, '', '', '{Ellipsis}', {SnippetTokens}) AS excerpt,
                       -{Bm25} AS score
                FROM chapter_fts
                WHERE chapter_fts MATCH $match
            ) AS matched
            JOIN chapter ON chapter.id = matched.id
            {ScopeClause(scope, "chapter.path", "WHERE")}
            ORDER BY matched.score DESC, chapter.path, chapter.line
            LIMIT $limit
            """;

        return Query(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$match", match);
                command.Parameters.AddWithValue("$limit", limit);
                BindScope(command, scope);
            },
            reader => new KnowledgeSearchRow(
                reader.GetString(0),
                Text(reader, 1),
                reader.GetString(2),
                Text(reader, 3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.GetDouble(5)));
    }

    /// <summary>
    /// Every stored vector produced by <paramref name="model"/>, with the chapter
    /// it belongs to.
    ///
    /// <para><b>The model filter is the point, not a convenience.</b> Embeddings
    /// pin a database to whichever model produced them, and two models' vectors
    /// live in the same coordinate space only by coincidence — a cosine between
    /// them is a number with no meaning, which is worse than no answer because it
    /// ranks. The database records what it was built with in
    /// <c>meta.embeddingModel</c>; this asks for the one the reader is configured
    /// for and ignores the rest, so a half-re-embedded corpus degrades to fewer
    /// results rather than to wrong ones.</para>
    ///
    /// <para>The join is on <c>content_hash</c> because that is what
    /// <c>chapter_embedding</c> is keyed by — so an unchanged chapter that moved
    /// is never re-embedded, and two chapters with identical text share one
    /// vector and both come back.</para>
    /// </summary>
    public IReadOnlyList<KnowledgeEmbeddingRow> Embeddings(string? model, string? scope)
    {
        if (string.IsNullOrWhiteSpace(model)) return [];

        var sql = $"""
            SELECT chapter.path, chapter.folder, chapter.slug, chapter.title, embedding.dimensions, embedding.vector
            FROM chapter_embedding AS embedding
            JOIN chapter ON chapter.content_hash = embedding.content_hash
            WHERE embedding.model = $model
            {ScopeClause(scope, "chapter.path", "AND")}
            ORDER BY chapter.path, chapter.line
            """;

        return Query(
            sql,
            command =>
            {
                command.Parameters.AddWithValue("$model", model);
                BindScope(command, scope);
            },
            reader => new KnowledgeEmbeddingRow(
                reader.GetString(0),
                Text(reader, 1),
                reader.GetString(2),
                Text(reader, 3),
                reader.GetInt32(4),
                ReadVector(reader.GetStream(5), reader.GetInt32(4))));
    }

    /// <summary>What <c>snippet</c> puts where it cut the text. Three dots rather
    /// than the ellipsis character, because the excerpt is shown in a proportional
    /// UI font beside Markdown that uses both.</summary>
    private const string Ellipsis = "...";

    /// <summary>Tokens of context around the match. Wide enough to show the
    /// sentence the words are in, short enough that the address stays the thing a
    /// reader's eye lands on.</summary>
    private const int SnippetTokens = 14;

    /// <summary>
    /// The scope filter as a clause fragment, or nothing at all for the
    /// repository scope.
    /// <para>
    /// The same membership rule the writer's own <c>projectScope</c> uses — the
    /// folder itself, or anything beneath it — spelled here as a leading keyword
    /// plus a condition so the two callers can each say whether it opens their
    /// WHERE or extends it.
    /// </para>
    /// </summary>
    private static string ScopeClause(string? scope, string column, string keyword) =>
        IsRepositoryScope(scope)
            ? string.Empty
            : $"{keyword} ({column} = $scope OR {column} LIKE $prefix ESCAPE '\\')";

    private static void BindScope(SqliteCommand command, string? scope)
    {
        if (IsRepositoryScope(scope)) return;

        command.Parameters.AddWithValue("$scope", scope!);
        command.Parameters.AddWithValue("$prefix", Escape(scope! + "/") + "%");
    }

    /// <summary>
    /// A stored vector's bytes as floats.
    ///
    /// <para>The layout is <paramref name="dimensions"/> IEEE-754 single-precision
    /// values, little-endian, packed with no header — the same thing a
    /// <c>Float32Array</c> serialises to, which is why the writer can produce one
    /// with no encoder of its own. It is written down in the header of
    /// <c>tools/knowledge/knowledge-schema.mjs</c> beside the table, because a
    /// byte layout agreed by two languages and stated in neither is precisely the
    /// silent drift ADR 0004 names as this change's main risk. Read explicitly
    /// rather than reinterpreted in place so the answer does not depend on the
    /// machine's own endianness.</para>
    ///
    /// <para>A row whose blob is not <paramref name="dimensions"/> floats long is
    /// handed back as it really is — short or long — and the ranking drops it. A
    /// truncated vector is not a vector, and padding it would invent a direction
    /// nobody embedded.</para>
    /// </summary>
    private static float[] ReadVector(Stream blob, int dimensions)
    {
        using (blob)
        {
            if (dimensions <= 0 || blob.Length != (long)dimensions * sizeof(float)) return [];

            var bytes = new byte[blob.Length];
            blob.ReadExactly(bytes);

            var vector = new float[dimensions];
            for (var index = 0; index < dimensions; index++)
            {
                vector[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float)));
            }

            return vector;
        }
    }
}

/// <summary>
/// One full-text hit: the chapter address, the words around the match, and
/// FTS5's own score for it.
/// </summary>
/// <param name="Excerpt">Raw from <c>snippet</c>, so it still carries the
/// Markdown and the line breaks of the source. Presenting it is the caller's
/// business.</param>
/// <param name="Score">Negated <c>bm25</c>, so larger is better. Comparable
/// within one answer and meaningless across two.</param>
public sealed record KnowledgeSearchRow(
    string Path,
    string? Folder,
    string Slug,
    string? Title,
    string Excerpt,
    double Score);

/// <summary>
/// One stored vector and the chapter it belongs to.
/// </summary>
/// <param name="Dimensions">What the writer recorded. <paramref name="Vector"/>
/// is empty when the blob did not hold that many floats.</param>
public sealed record KnowledgeEmbeddingRow(
    string Path,
    string? Folder,
    string Slug,
    string? Title,
    int Dimensions,
    IReadOnlyList<float> Vector);
