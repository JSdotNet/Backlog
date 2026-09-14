namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// Retrieval by what a chapter means, so a recollection that does not share the
/// author's vocabulary still lands.
///
/// <para><b>Why this is a port at all.</b> Today the only implementation scans
/// every stored vector and computes a cosine, and that is the right answer for
/// this corpus: 639 chapters, a few thousand vectors, a scan that costs less than
/// the query which fetched them. The alternative — <c>sqlite-vec</c> or an
/// equivalent — is a native loadable extension, which is a real deployment
/// problem inside an MSIX package and on Android, bought three orders of
/// magnitude below where an approximate index starts paying. The port is here so
/// that when the corpus does grow, an index can arrive behind it without a single
/// caller moving. Local ADR 0004 says exactly this under "No vector index
/// yet".</para>
///
/// <para><b>Additive, never a precondition.</b> The semantic tier needs a model
/// and is versioned by it, so it is optional by construction: a database with no
/// vectors answers nothing here and <see cref="IKnowledgeSearch"/> still answers
/// everything. That is an empty result rather than
/// <see cref="KnowledgeSearchAnswer.Unavailable"/> — search by meaning being
/// absent is a rung of the ladder, and only "no index at all" is the rung that
/// speaks to the user.</para>
///
/// <para><b>The query vector arrives embedded.</b> Turning a reader's words into
/// a vector needs the same model the corpus was embedded with, and reaching a
/// model is a network call this reader must never make on a panel's path. So the
/// caller brings the vector and this port only ranks — which also means the
/// ranking is testable without a model at all.</para>
/// </summary>
public interface IKnowledgeVectorSearch
{
    /// <summary>
    /// The chapters whose stored vectors point most nearly the same way as
    /// <paramref name="queryVector"/>, most similar first.
    /// </summary>
    /// <param name="queryVector">The question, embedded by the same model the
    /// database is pinned to. A vector of a different width, or produced by a
    /// different model, cannot be compared with these and is not.</param>
    /// <param name="repositoryAlias">Which repository's knowledge to search, or
    /// <see langword="null"/> for the storage-folder scope.</param>
    /// <param name="scope">One knowledge folder, or <see langword="null"/> for
    /// every area at once.</param>
    /// <param name="limit">How many hits to return at most.</param>
    KnowledgeSearchAnswer Nearest(
        IReadOnlyList<float> queryVector,
        string? repositoryAlias = null,
        string? scope = null,
        int limit = 30);
}
