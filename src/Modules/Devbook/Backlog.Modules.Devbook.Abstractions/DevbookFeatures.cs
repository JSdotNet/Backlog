namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The feature keys Devbook owns.
/// <para>
/// Each key names something about the devbook rather than about the screen
/// asking, so it sits here and the Shell reads it across — the direction that
/// works, because nothing below the Shell may read the Shell.
/// </para>
/// </summary>
public static class DevbookFeatures
{
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
    /// the unit is (local ADR 0047), instead of the Domain area as it reads today.
    /// <para>
    /// A devbook key by the same rule as its neighbours. Only the Domain area reads
    /// it: the other areas do not. It ships off while the plan <c>+devbook-domain-view</c> builds
    /// the view, and is retired when the old Domain view is removed.
    /// </para></summary>
    public const string DomainView = "devbook-domain-view";

    /// <summary>Map the Brain and every registered devbook as brains in one
    /// full-window atlas (<c>.devbook/domain/brain/context.md#lobes-atlas</c>).
    /// <para>
    /// A devbook key by the same rule as its neighbours: the pane's own Atlas
    /// button reads it to choose which atlas it opens, and the Shell reads it to
    /// decide whether the header offers the atlas at all.
    /// </para></summary>
    public const string LobesAtlas = "atlas-lobes";
}
