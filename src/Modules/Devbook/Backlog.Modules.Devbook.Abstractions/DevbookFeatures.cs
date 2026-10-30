namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The feature keys Devbook owns.
/// <para>
/// <see cref="RepositoryDevbook"/> is read by this context's own scope service
/// and by the Shell that decides whether to offer the pane at all. It names
/// something about the devbook rather than about the screen asking, so it sits
/// here and the Shell reads it across — the direction that works, because
/// nothing below the Shell may read the Shell.
/// </para>
/// </summary>
public static class DevbookFeatures
{
    /// <summary>Show the Devbook side pane and its design, architecture, domain,
    /// technology, AI adoption and instruction sections.
    /// <para>
    /// One switch where there were two: <c>devbook-sections</c> gated the sections
    /// and this key the pane, and neither meant anything without the other — a pane
    /// with its sections off offered nothing to read. The catalog names the former
    /// key, so a choice made under it carries over.
    /// </para></summary>
    public const string RepositoryDevbook = "repository-devbook";

    /// <summary>Draw a chapter diagram from its generated Archify artifact where
    /// one exists, instead of rendering the mermaid.
    /// <para>
    /// A devbook key rather than a shell one, by the same rule as its
    /// neighbours: what it switches is how a devbook chapter's diagrams are
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
    /// <c>.devbook/arc42/workspace.dsl</c>. Someone who wants richer pictures of the fences they
    /// have should not have to take a whole second model with them.
    /// </para></summary>
    public const string C4Diagrams = "c4-diagrams";

    /// <summary>Search the knowledge areas by the words their chapters use.
    /// <para>
    /// A devbook key by the same rule as its neighbours: what it switches on is
    /// a way of reading this context's chapters. It is the one capability that a
    /// repository nobody has generated an index for cannot offer at all, which is
    /// why the surface behind it says so in words rather than showing an empty
    /// list — see <c>DevbookSearchAnswer.UnavailableMessage</c>.
    /// </para></summary>
    public const string Search = "devbook-search";

    /// <summary>Search the knowledge areas by what their chapters mean, beside
    /// searching by their words.
    /// <para>
    /// Its own key rather than a second meaning for <see cref="Search"/>, because
    /// the two are not one switch: retrieval by meaning needs an embedding model,
    /// pins the database to whichever model produced its vectors, and is additive
    /// — with it off, or with no vectors stored, search by words still answers.
    /// Wired and making no live call yet.
    /// </para></summary>
    public const string SemanticSearch = "devbook-semantic-search";

    /// <summary>Offer Edit on a repository devbook chapter, which swaps the
    /// rendered chapter for its editor.
    /// <para>
    /// Its own key rather than a second meaning for <see cref="Status"/>, because
    /// the two are weighed for removal separately: someone may keep the status a
    /// chapter states and lose the means of rewriting the chapter, or the other way
    /// round. The Brain is outside it — a note there is always editable — so the
    /// switch reads as the repository viewer's, not as Devbook-wide.
    /// </para></summary>
    public const string Editing = "devbook-editing";

    /// <summary>Show the status every devbook file and each of its chapters state,
    /// Brain notes included — the selector or badge beside the name and beside
    /// each heading, with the decision state that qualifies it.
    /// <para>
    /// It switches the file and chapter surfaces only. The AI loop's usage pills
    /// and the atlas badges read a chapter's status as a summary of somewhere
    /// else, not beside the chapter, and are not behind it.
    /// </para>
    /// <para>
    /// Its own key rather than folded into <see cref="Editing"/>, by the same
    /// reasoning in reverse: the status is read, not written, wherever the viewer
    /// cannot change it, so it has a life of its own apart from Edit. The other
    /// fields a block states — related, sync — are not status and stay drawn.
    /// </para></summary>
    public const string Status = "devbook-status";

    /// <summary>Read the Domain area one unit at a time — the context map, each
    /// bounded context, each file inside it — on a page drawn for the kind of file
    /// the unit is (local ADR 0044), instead of the Domain area as it reads today.
    /// <para>
    /// A devbook key by the same rule as its neighbours. Only the Domain area reads
    /// it: the other areas, and <see cref="RepositoryDevbook"/> that offers the pane
    /// at all, do not. It ships off while the plan <c>+devbook-domain-view</c> builds
    /// the view, and is retired when the old Domain view is removed.
    /// </para></summary>
    public const string DomainView = "devbook-domain-view";

    /// <summary>Map the Brain and every registered devbook as brains in one
    /// full-window atlas (<c>.devbook/domain/devbook/context.md#lobes-atlas</c>).
    /// <para>
    /// A devbook key by the same rule as its neighbours: the pane's own Atlas
    /// button reads it to choose which atlas it opens, and the Shell reads it to
    /// decide whether the header offers the atlas at all.
    /// </para></summary>
    public const string LobesAtlas = "atlas-lobes";
}
