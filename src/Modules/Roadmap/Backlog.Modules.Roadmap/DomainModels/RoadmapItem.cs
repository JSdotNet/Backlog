using Backlog.Modules.Roadmap.Abstractions;

namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// One piece of planned work inside a <see cref="RoadmapPlan"/>.
/// <para>
/// It has no status and no percentage. Both are questions about execution, and
/// execution belongs to Backlog Management: an item that names an entry shows that
/// entry's progress, and an item that names none shows that it is planned and
/// nothing more.
/// </para>
/// <para>
/// Every mutator is <c>internal</c> on purpose. An item is not a consistency
/// boundary — its most important rule, that dependencies never form a cycle, is
/// only decidable with the whole graph in view — so changes arrive through the
/// plan, which is the only thing that can see enough to allow them.
/// </para>
/// </summary>
public sealed class RoadmapItem
{
    /// <summary>Full constructor, used by <see cref="RoadmapPlan"/> to add an item
    /// and by storage to rehydrate a persisted one. Public for the second reason
    /// only — an adapter in another assembly has to be able to rebuild what it
    /// wrote — which is why every way of <em>changing</em> an item below is not.</summary>
    public RoadmapItem(
        Guid id,
        string title,
        PlannedWindow window,
        PlanningPriority priority,
        RepositoryScope scope,
        PlanningLane lane,
        Dependencies dependencies,
        Guid? backlogEntryId,
        string? notes,
        PlanningTag? tag = null,
        KnowledgeReferences? knowledgeRefs = null)
    {
        Id = id;
        Title = title;
        Window = window;
        Priority = priority;
        Scope = scope;
        Lane = lane;
        Dependencies = dependencies;
        BacklogEntryId = backlogEntryId;
        Notes = notes;
        // Derived from the title when none was given, so every item — freshly added,
        // rehydrated, or read from a plan.json written before tags existed — always has
        // one. Done here rather than in each caller so the guarantee has a single home.
        Tag = tag ?? PlanningTag.From(title);
        KnowledgeRefs = knowledgeRefs ?? KnowledgeReferences.Empty;
    }

    /// <summary>Stable across every reschedule. That is what makes it safe for a
    /// dependency, or another context, to keep as a foreign id.</summary>
    public Guid Id { get; }

    public string Title { get; private set; }

    public PlannedWindow Window { get; private set; }

    /// <summary>The plan's own priority. Setting it never writes to a linked
    /// backlog entry, and that entry's priority never overwrites this.</summary>
    public PlanningPriority Priority { get; private set; }

    public RepositoryScope Scope { get; private set; }

    public PlanningLane Lane { get; private set; }

    public Dependencies Dependencies { get; }

    /// <summary>The entry that executes this item, when there is one. A foreign id
    /// and nothing more: it may dangle, and a dangling link reads as unlinked
    /// rather than as an error.</summary>
    public Guid? BacklogEntryId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>The slug this item is known by wherever tags are used. Every item has
    /// one; it is derived from the title when the item is first given none, and a
    /// rename never moves it — see <see cref="PlanningTag"/> for why that boundary
    /// matters.</summary>
    public PlanningTag Tag { get; private set; }

    /// <summary>The knowledge chapters this item points at, as opaque references that
    /// may dangle. Empty means it points at none.</summary>
    public KnowledgeReferences KnowledgeRefs { get; private set; }

    internal void Rename(string title) => Title = title;

    /// <summary>Moves the item in time. The lane is only changed when one is
    /// given: dropping something on a different row without travelling in time
    /// must not also snap its dates, and moving it in time must not silently
    /// refile it.</summary>
    internal void MoveTo(PlannedWindow window, PlanningLane? lane = null)
    {
        Window = window;
        if (lane is not null) Lane = lane;
    }

    internal void Prioritise(PlanningPriority priority) => Priority = priority;

    internal void FileUnder(RepositoryScope scope) => Scope = scope;

    internal void LinkTo(Guid? backlogEntryId) => BacklogEntryId = backlogEntryId;

    internal void Annotate(string? notes) => Notes = notes;

    /// <summary>Replaces the tag. Deliberately its own operation and never a
    /// consequence of <see cref="Rename"/>: moving the tag is a decision, because
    /// something elsewhere may already be pointing at the old one.</summary>
    internal void Retag(PlanningTag tag) => Tag = tag;

    internal void ReferenceKnowledge(KnowledgeReferences knowledgeRefs) => KnowledgeRefs = knowledgeRefs;
}
