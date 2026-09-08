namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// The feature keys Second Brain owns.
/// <para>
/// <see cref="KnowledgeSections"/> is read by this context's own scope service
/// and <see cref="RepositoryKnowledge"/> by the Shell that decides whether to
/// offer the pane at all. Both name something about knowledge rather than about
/// the screen asking, so both sit here and the Shell reads them across — the
/// direction that works, because nothing below the Shell may read the Shell.
/// </para>
/// </summary>
public static class KnowledgeFeatures
{
    /// <summary>Show the side pane for repository knowledge.</summary>
    public const string RepositoryKnowledge = "repository-knowledge";

    /// <summary>Show the design, architecture, domain, technology and
    /// instruction sections in the knowledge pane and header.</summary>
    public const string KnowledgeSections = "knowledge-sections";

    /// <summary>Draw a chapter diagram from its generated Archify artifact where
    /// one exists, instead of rendering the mermaid.
    /// <para>
    /// A knowledge key rather than a shell one, by the same rule as its
    /// neighbours: what it switches is how a knowledge chapter's diagrams are
    /// drawn, so it belongs with the context the chapters are of. The Shell only
    /// contributes the catalog row that names and describes it, and the adapter
    /// that answers it lives with the panels that render the chapters.
    /// </para></summary>
    public const string ArchifyDiagrams = "archify-diagrams";

    /// <summary>Show the C4 model that sits beside the architecture chapters, and
    /// the references between the two.
    /// <para>
    /// Its own key rather than a second meaning for <see cref="ArchifyDiagrams"/>,
    /// because the two switch unrelated things. Archify changes how a chapter's own
    /// mermaid fence is drawn; this adds views that are not in any chapter and are
    /// authored somewhere else entirely — in c4hero, as Structurizr DSL under
    /// <c>.arc42/_c4/</c>. Someone who wants richer pictures of the fences they
    /// have should not have to take a whole second model with them.
    /// </para></summary>
    public const string C4Diagrams = "c4-diagrams";

    /// <summary>Search the knowledge areas by the words their chapters use.
    /// <para>
    /// A knowledge key by the same rule as its neighbours: what it switches on is
    /// a way of reading this context's chapters. It is the one capability that a
    /// repository nobody has generated an index for cannot offer at all, which is
    /// why the surface behind it says so in words rather than showing an empty
    /// list — see <c>KnowledgeSearchAnswer.UnavailableMessage</c>.
    /// </para></summary>
    public const string Search = "knowledge-search";

    /// <summary>Search the knowledge areas by what their chapters mean, beside
    /// searching by their words.
    /// <para>
    /// Its own key rather than a second meaning for <see cref="Search"/>, because
    /// the two are not one switch: retrieval by meaning needs an embedding model,
    /// pins the database to whichever model produced its vectors, and is additive
    /// — with it off, or with no vectors stored, search by words still answers.
    /// Wired and making no live call yet.
    /// </para></summary>
    public const string SemanticSearch = "knowledge-semantic-search";
}
