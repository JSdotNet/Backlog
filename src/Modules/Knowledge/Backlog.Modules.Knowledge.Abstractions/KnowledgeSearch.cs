namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// Where a result came from, said the way everything else in this product says
/// it: a repository-relative path and the heading slug beneath it.
///
/// <para>This is the whole point of the retrieval surface rather than a label on
/// it. <c>.domain/second-brain/features.md#knowledge-retrieval</c> is explicit —
/// results name the chapter they came from rather than returning loose text,
/// "because a chapter address is what every other part of this context already
/// links by". A <c>related</c> field, a <c>depends-on</c> edge, a reference in
/// prose and a graph node all spell a chapter the same way, so a hit that carries
/// the address is a hit the pane can open, the atlas can find and an assistant can
/// cite. A hit that carried only text would be the one thing in this context that
/// could not be linked to.</para>
/// </summary>
/// <param name="Path">Repository-relative, <c>/</c>-separated, as the generator
/// spells it.</param>
/// <param name="Slug">The heading's slug, or the empty string for a chapter that
/// is the file itself.</param>
/// <param name="Title">The heading as authored, when there is one.</param>
/// <param name="Folder">The knowledge folder kind — <c>domain</c>, <c>arc42</c> —
/// so a caller can group by area without splitting the path.</param>
public sealed record KnowledgeChapterAddress(string Path, string Slug, string? Title, string? Folder)
{
    /// <summary>The address as one string, which is exactly the form a
    /// <c>related</c> entry, a graph node id and a reference in prose already
    /// carry.</summary>
    public string Reference => string.IsNullOrEmpty(Slug) ? Path : $"{Path}#{Slug}";
}

/// <summary>
/// One result: the chapter, why it matched, and how well.
/// </summary>
/// <param name="Chapter">The address. This is the result.</param>
/// <param name="Excerpt">A few words around the match, to show a reader why this
/// chapter is in the list. It is context for the address and never a substitute
/// for it — nothing links by an excerpt, and no caller should try.</param>
/// <param name="Score">Higher is better. The scale is the retrieval tier's own
/// and is comparable only within one answer, which is why nothing outside this
/// record interprets it.</param>
public sealed record KnowledgeSearchHit(KnowledgeChapterAddress Chapter, string Excerpt, double Score);

/// <summary>
/// What a search surface gets back, including the one case where the honest
/// answer is a sentence rather than a list.
///
/// <para>Local ADR 0004's ladder degrades quietly everywhere except here.
/// Browsing falls back to Markdown because browsing touches the handful of files
/// on screen; search cannot, because scanning the corpus per query is not a
/// slower answer but a hang. So a repository with no generated index has no
/// search, and <see cref="UnavailableMessage"/> is how that arrives — a sentence
/// naming what is missing and the command that fixes it, never an empty list,
/// which reads as "nothing matched" and is a different claim entirely.</para>
///
/// <para>The port deliberately does not restate the reader's tier enum. Which
/// rung a database is on is a fact about a file that has been opened, and it is
/// the reader's word — <c>KnowledgeRetrievalTier</c>, in
/// <c>Backlog.Infrastructure.Knowledge</c>. What a caller of this port needs is
/// the answer and, when there is none, the sentence to show.</para>
/// </summary>
public sealed record KnowledgeSearchAnswer(IReadOnlyList<KnowledgeSearchHit> Hits, string? UnavailableMessage)
{
    /// <summary>Nothing matched, which is a real and ordinary answer.</summary>
    public static KnowledgeSearchAnswer None { get; } = new([], null);

    /// <summary>There was nothing to search, which is not.</summary>
    public static KnowledgeSearchAnswer Unavailable(string message) => new([], message);

    public static KnowledgeSearchAnswer For(IReadOnlyList<KnowledgeSearchHit> hits) => new(hits, null);

    /// <summary>Whether this answer is "there is no index" rather than "no chapter
    /// says that".</summary>
    public bool IsUnavailable => UnavailableMessage is not null;
}

/// <summary>
/// Retrieval by the words a chapter uses.
///
/// <para>Synchronous on purpose. The index is a local file and one query reads a
/// bounded number of rows out of it, so an asynchronous facade over a synchronous
/// read would buy a caller nothing and cost every one of them a state machine —
/// the same reasoning that keeps the rest of the knowledge reader synchronous.</para>
///
/// <para>The port exists so the tier behind it can change without moving its
/// callers. Today it is FTS5 inside the generated database; the pane asks this
/// and never asks SQLite.</para>
/// </summary>
public interface IKnowledgeSearch
{
    /// <summary>
    /// The chapters whose words match <paramref name="query"/>, best first.
    /// </summary>
    /// <param name="query">What the reader typed. Free text: the adapter is
    /// responsible for turning it into whatever its tier's query language wants,
    /// and for refusing to hand a reader's punctuation to a parser.</param>
    /// <param name="repositoryAlias">Which repository's knowledge to search, or
    /// <see langword="null"/> for the storage-folder scope.</param>
    /// <param name="scope">One knowledge folder — <c>.domain</c> — or
    /// <see langword="null"/> for every area at once, which is the default the
    /// domain asks for: "find the chapter that answers a question across every
    /// area and every note at once".</param>
    /// <param name="limit">How many hits to return at most.</param>
    KnowledgeSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30);
}
