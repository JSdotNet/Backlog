using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>The embedding model this reader is configured for, and why it is that
/// one.</summary>
public static class KnowledgeEmbeddingModel
{
    /// <summary>
    /// <c>text-embedding-3-small</c> — the deployment
    /// <c>infra/foundry/main.bicep</c> adds behind <c>includeEmbeddingModel</c>.
    ///
    /// <para>Small rather than large on purpose: 1536 dimensions against 3072
    /// halves both the scan cost of the brute-force cosine and the size of the
    /// database, for a quality difference that barely shows on a documentation
    /// corpus which FTS5 is answering alongside it.</para>
    ///
    /// <para>It is the reader's <i>configuration</i>, not a fact about any
    /// particular database. What a database was actually built with is in its own
    /// <c>meta.embeddingModel</c>, and where the two disagree the vectors are
    /// ignored — see <see cref="KnowledgeSemanticSearch"/>.</para>
    /// </summary>
    public const string Default = "text-embedding-3-small";
}

/// <summary>
/// <see cref="IKnowledgeVectorSearch"/> over <c>chapter_embedding</c>, by
/// brute-force cosine.
///
/// <para><b>The model check is the substance of this class.</b> A database is
/// pinned to whatever produced its vectors: two models' embeddings share a
/// coordinate space only by coincidence, so a cosine across them is not an
/// approximate answer but a meaningless one that still sorts — the worst failure
/// available here, because it looks exactly like working. The reader therefore
/// asks for the model it is configured for and ignores every other vector in the
/// file, which makes a partly re-embedded corpus return fewer results instead of
/// wrong ones.</para>
///
/// <para><b>Empty is not unavailable.</b> A database with no vectors for this
/// model answers nothing, and that is rung five of ADR 0004's ladder: full-text
/// search answers, search by meaning is absent rather than broken. Only rung six
/// — no database at all — is the one that speaks to the user, and this reports it
/// with the same sentence <see cref="KnowledgeFullTextSearch"/> uses, because it
/// is the same missing file.</para>
/// </summary>
/// <param name="model">Which vectors this reader will compare against. Injected
/// rather than fixed so a repository embedded with something else can be read by
/// pointing the reader at it, without a second implementation.</param>
public sealed class KnowledgeSemanticSearch(IKnowledgeFolderSource folders, string? model = null) : IKnowledgeVectorSearch
{
    private const string Surface = "Search by meaning";

    private readonly string _model = string.IsNullOrWhiteSpace(model) ? KnowledgeEmbeddingModel.Default : model;

    /// <summary>The model this reader compares against, for a caller that has to
    /// embed the question with the same one.</summary>
    public string Model => _model;

    public KnowledgeSearchAnswer Nearest(
        IReadOnlyList<float> queryVector,
        string? repositoryAlias = null,
        string? scope = null,
        int limit = 30)
    {
        ArgumentNullException.ThrowIfNull(queryVector);

        using var database = KnowledgeDatabaseSource.TryOpen(folders, repositoryAlias);

        if (database is null)
        {
            return KnowledgeSearchAnswer.Unavailable(KnowledgeRetrieval.UnavailableMessage(Surface));
        }

        if (queryVector.Count == 0 || limit <= 0) return KnowledgeSearchAnswer.None;

        var query = queryVector as float[] ?? [.. queryVector];

        var hits = database.Embeddings(_model, scope)
            .Select(row => new KnowledgeSearchHit(
                new KnowledgeChapterAddress(row.Path, row.Slug, row.Title, row.Folder),
                string.Empty,
                KnowledgeVectorMath.Cosine(query, AsSpan(row.Vector))))
            // Zero is what Cosine answers for a comparison it could not make - a
            // width that does not match the question, or a stored vector with no
            // direction. Dropping them rather than ranking them last keeps "no
            // comparable vectors" answering nothing at all, which is the truth,
            // instead of a list of chapters in arbitrary order.
            .Where(hit => hit.Score > 0)
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Chapter.Reference, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        return KnowledgeSearchAnswer.For(hits);
    }

    /// <summary>The row's vector as a span, without copying the common case. The
    /// reader builds these as arrays; anything else is copied rather than
    /// refused.</summary>
    private static ReadOnlySpan<float> AsSpan(IReadOnlyList<float> vector) =>
        vector as float[] ?? [.. vector];
}
