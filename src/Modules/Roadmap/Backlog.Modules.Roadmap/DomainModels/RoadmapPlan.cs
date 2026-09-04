using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// Aggregate root of the Roadmap Planning bounded context: the single plan for the
/// workspace, and the consistency boundary for every item, milestone, and
/// dependency in it.
/// <para>
/// The plan is the root rather than the item, for one reason that outweighs the
/// convenience of a smaller boundary: acyclicity is an invariant <em>across</em>
/// items, not within one. An item cannot tell whether the dependency it is about to
/// accept closes a loop three items away, so a boundary drawn around a single item
/// would push its most important rule outside the model. The plan is also the unit
/// that is read and written as a whole, which makes the boundary and the
/// transaction the same shape.
/// </para>
/// <para>
/// Every refusal comes back as a <see cref="Result"/> rather than an exception. A
/// circular dependency, a date that does not make a window, an id that is no longer
/// there — each of those is something a person does by accident while planning,
/// and per inherited ADR 0004 an outcome the caller is expected to handle is
/// data, not a throw.
/// </para>
/// <para>
/// A plan with nothing in it is a valid plan: a first run, or everything delivered.
/// </para>
/// </summary>
public sealed class RoadmapPlan
{
    private readonly List<RoadmapItem> _items;
    private readonly List<Milestone> _milestones;
    private readonly BandColours _bandColours;

    private RoadmapPlan(List<RoadmapItem> items, List<Milestone> milestones, BandColours bandColours)
    {
        _items = items;
        _milestones = milestones;
        _bandColours = bandColours;
    }

    public static RoadmapPlan Empty() => new([], [], BandColours.None());

    /// <summary>
    /// Rebuilds a plan from what storage previously wrote.
    /// <para>
    /// Entries sharing an id are collapsed to the first of them, and dependencies
    /// naming nothing in the plan are dropped. Neither can happen through this
    /// module — but the plan is one hand-editable file, and a plan that refuses to
    /// load because somebody duplicated a block while editing it is worse than a
    /// plan that loads without the duplicate.
    /// </para>
    /// </summary>
    public static RoadmapPlan Rehydrate(
        IEnumerable<RoadmapItem>? items,
        IEnumerable<Milestone>? milestones,
        BandColours? bandColours = null)
    {
        var loadedItems = Distinct(items ?? [], item => item.Id);
        var loadedMilestones = Distinct(milestones ?? [], milestone => milestone.Id);

        var known = loadedItems.Select(item => item.Id)
            .Concat(loadedMilestones.Select(milestone => milestone.Id))
            .ToHashSet();

        foreach (var dependencies in loadedItems.Select(item => item.Dependencies)
                     .Concat(loadedMilestones.Select(milestone => milestone.Dependencies)))
        {
            foreach (var dangling in dependencies.All.Where(id => !known.Contains(id)).ToList())
            {
                dependencies.Remove(dangling);
            }
        }

        return new RoadmapPlan(loadedItems, loadedMilestones, bandColours ?? BandColours.None());
    }

    public IReadOnlyList<RoadmapItem> Items => _items;

    public IReadOnlyList<Milestone> Milestones => _milestones;

    public bool IsEmpty => _items.Count == 0 && _milestones.Count == 0;

    /// <summary>Which colour each repository's band has been given, for the ones
    /// somebody has chosen. A repository absent from this has not been chosen for and
    /// is left to the view to place.</summary>
    public BandColours BandColours => _bandColours;

    /// <summary>Every item and milestone as a dependency endpoint, which is the
    /// only shape the graph questions need.</summary>
    public IReadOnlyList<PlanNode> Nodes() =>
    [
        .. _items.Select(item => new PlanNode(item.Id, item.Title, item.Window.Start, item.Window.End, item.Dependencies.All)),
        .. _milestones.Select(milestone => new PlanNode(milestone.Id, milestone.Title, milestone.On, milestone.On, milestone.Dependencies.All))
    ];

    /// <summary>Everywhere the plan currently disagrees with itself. Reported,
    /// never corrected.</summary>
    public IReadOnlyList<PlanContradiction> Contradictions() => PlanSequencing.Contradictions(Nodes());

    /// <summary>
    /// The distinct tags in use across the plan's items, in the order they first
    /// appear. A plain query rather than an index: it is asked for on the read path
    /// and answered from the items, so there is nothing to keep in step with them.
    /// <para>
    /// A tag is not unique across items — two may deliberately share one to be read as
    /// a group. This is what lets a caller notice that: pair it with
    /// <see cref="ItemsTagged"/> to see which items a shared tag gathers.
    /// </para>
    /// </summary>
    public IReadOnlyList<PlanningTag> TagsInUse()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. _items.Select(item => item.Tag).Where(tag => seen.Add(tag.Value))];
    }

    /// <summary>The items carrying a given tag, in plan order. Empty when nothing does.
    /// More than one is not a fault: a shared tag is a deliberate grouping.</summary>
    public IReadOnlyList<RoadmapItem> ItemsTagged(PlanningTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return [.. _items.Where(item => item.Tag == tag)];
    }

    public Result<RoadmapItem> AddItem(
        string title,
        PlannedWindow window,
        PlanningPriority priority = PlanningPriority.Medium,
        RepositoryScope? scope = null,
        PlanningLane? lane = null,
        Guid? taskId = null,
        string? notes = null,
        PlanningTag? tag = null,
        KnowledgeReferences? knowledgeRefs = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (string.IsNullOrWhiteSpace(title)) return Result.Failure<RoadmapItem>(RoadmapErrors.TitleRequired());

        var item = new RoadmapItem(
            Guid.NewGuid(),
            title.Trim(),
            window,
            priority,
            scope ?? RepositoryScope.Unfiled,
            lane ?? PlanningLane.Default,
            Dependencies.None(),
            taskId,
            notes,
            // Null means "derive one from the title"; the item does that for itself so
            // there is a single home for the rule.
            tag,
            knowledgeRefs ?? KnowledgeReferences.Empty);

        _items.Add(item);
        return Result.Success(item);
    }

    /// <summary>Moves an item in time, and to another lane when one is given.
    /// Dependencies are deliberately left alone: the plan is allowed to contradict
    /// itself, and dragging the dependent work along would hide the fact worth
    /// seeing.</summary>
    public Result<RoadmapItem> Reschedule(Guid itemId, PlannedWindow window, PlanningLane? lane = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        var item = FindItem(itemId);
        if (item is null) return Result.Failure<RoadmapItem>(RoadmapErrors.ItemNotFound(itemId));

        item.MoveTo(window, lane);
        return Result.Success(item);
    }

    public Result<RoadmapItem> Prioritise(Guid itemId, PlanningPriority priority)
    {
        var item = FindItem(itemId);
        if (item is null) return Result.Failure<RoadmapItem>(RoadmapErrors.ItemNotFound(itemId));

        item.Prioritise(priority);
        return Result.Success(item);
    }

    /// <summary>
    /// Applies everything an editor submits at once: title, window, priority, scope,
    /// lane, link and notes.
    /// <para>
    /// One method rather than six, because a form is one decision. Six calls would
    /// let a plan sit half-edited if the third were refused, and the caller would
    /// have to know which order to make them in to avoid it. Nothing is optional
    /// here for the same reason: an editor that omitted a field would be saying
    /// "leave it alone", and the difference between that and "clear it" is exactly
    /// the ambiguity that loses somebody's notes.
    /// </para>
    /// <para>
    /// Dependencies are deliberately not part of this. They are the one thing on an
    /// item that can be refused for a reason other than its own contents, so they
    /// keep their own operations and their own answers.
    /// </para>
    /// </summary>
    public Result<RoadmapItem> UpdateItem(
        Guid itemId,
        string title,
        PlannedWindow window,
        PlanningPriority priority,
        RepositoryScope? scope = null,
        PlanningLane? lane = null,
        Guid? taskId = null,
        string? notes = null,
        PlanningTag? tag = null,
        KnowledgeReferences? knowledgeRefs = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (string.IsNullOrWhiteSpace(title)) return Result.Failure<RoadmapItem>(RoadmapErrors.TitleRequired());

        var item = FindItem(itemId);
        if (item is null) return Result.Failure<RoadmapItem>(RoadmapErrors.ItemNotFound(itemId));

        item.Rename(title.Trim());
        item.MoveTo(window, lane ?? PlanningLane.Default);
        item.Prioritise(priority);
        item.FileUnder(scope ?? RepositoryScope.Unfiled);
        item.LinkTo(taskId);
        item.Annotate(notes);
        // The tag is set from what was submitted rather than recomputed from the new
        // title: an edit is where a tag moves, and a rename is not. A submission with
        // no tag means "derive one from the title" — the field was cleared on purpose.
        item.Retag(tag ?? PlanningTag.From(title.Trim()));
        item.ReferenceKnowledge(knowledgeRefs ?? KnowledgeReferences.Empty);

        return Result.Success(item);
    }

    public Result<RoadmapItem> Rename(Guid itemId, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Result.Failure<RoadmapItem>(RoadmapErrors.TitleRequired());

        var item = FindItem(itemId);
        if (item is null) return Result.Failure<RoadmapItem>(RoadmapErrors.ItemNotFound(itemId));

        item.Rename(title.Trim());
        return Result.Success(item);
    }

    /// <summary>Removes an item, and every dependency anywhere in the plan that
    /// pointed at it — an arrow to something that is gone is not a weaker plan, it
    /// is a wrong one.</summary>
    public Result RemoveItem(Guid itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return Result.Failure(RoadmapErrors.ItemNotFound(itemId));

        _items.Remove(item);
        ForgetDependenciesOn(itemId);
        return Result.Success();
    }

    public Result<Milestone> AddMilestone(
        string title,
        DateOnly on,
        MilestoneKind kind = MilestoneKind.Release,
        RepositoryScope? scope = null,
        PlanningLane? lane = null,
        bool isPlanWide = false)
    {
        if (string.IsNullOrWhiteSpace(title)) return Result.Failure<Milestone>(RoadmapErrors.TitleRequired());

        var milestone = new Milestone(
            Guid.NewGuid(),
            title.Trim(),
            on,
            kind,
            scope ?? RepositoryScope.Unfiled,
            lane ?? PlanningLane.Default,
            Dependencies.None(),
            isPlanWide);

        _milestones.Add(milestone);
        return Result.Success(milestone);
    }

    /// <summary>
    /// Applies everything a milestone editor submits at once, for the same reason
    /// <see cref="UpdateItem"/> does: a form is one decision, and a field left out of a
    /// partial update cannot say whether it was meant to be left alone or emptied.
    /// </summary>
    public Result<Milestone> UpdateMilestone(
        Guid milestoneId,
        string title,
        DateOnly on,
        MilestoneKind kind,
        RepositoryScope? scope = null,
        PlanningLane? lane = null,
        bool isPlanWide = false)
    {
        if (string.IsNullOrWhiteSpace(title)) return Result.Failure<Milestone>(RoadmapErrors.TitleRequired());

        var milestone = FindMilestone(milestoneId);
        if (milestone is null) return Result.Failure<Milestone>(RoadmapErrors.NodeNotFound(milestoneId));

        milestone.Rename(title.Trim());
        milestone.MoveTo(on);
        milestone.Reclassify(kind);
        milestone.FileUnder(scope ?? RepositoryScope.Unfiled);
        milestone.MoveToLane(lane ?? PlanningLane.Default);
        milestone.ReadAgainstThePlan(isPlanWide);

        return Result.Success(milestone);
    }

    public Result<Milestone> MoveMilestone(Guid milestoneId, DateOnly on)
    {
        var milestone = FindMilestone(milestoneId);
        if (milestone is null) return Result.Failure<Milestone>(RoadmapErrors.NodeNotFound(milestoneId));

        milestone.MoveTo(on);
        return Result.Success(milestone);
    }

    public Result RemoveMilestone(Guid milestoneId)
    {
        var milestone = FindMilestone(milestoneId);
        if (milestone is null) return Result.Failure(RoadmapErrors.NodeNotFound(milestoneId));

        _milestones.Remove(milestone);
        ForgetDependenciesOn(milestoneId);
        return Result.Success();
    }

    /// <summary>
    /// Records that <paramref name="dependsOnId"/> has to land before
    /// <paramref name="nodeId"/> can. Either end may be an item or a milestone.
    /// <para>
    /// Refused, with the plan left exactly as it was, when either end is not in this
    /// plan, when a node would wait for itself, or when the edge would close a
    /// cycle. Declaring a dependency that is already recorded succeeds and changes
    /// nothing.
    /// </para>
    /// </summary>
    public Result AddDependency(Guid nodeId, Guid dependsOnId)
    {
        var dependencies = DependenciesOf(nodeId);
        if (dependencies is null) return Result.Failure(RoadmapErrors.NodeNotFound(nodeId));
        if (DependenciesOf(dependsOnId) is null) return Result.Failure(RoadmapErrors.NodeNotFound(dependsOnId));
        if (nodeId == dependsOnId) return Result.Failure(RoadmapErrors.SelfDependency());
        if (dependencies.Contains(dependsOnId)) return Result.Success();

        if (PlanSequencing.WouldCycle(Nodes(), nodeId, dependsOnId))
        {
            return Result.Failure(RoadmapErrors.CyclicDependency(TitleOf(nodeId), TitleOf(dependsOnId)));
        }

        dependencies.Add(dependsOnId);
        return Result.Success();
    }

    /// <summary>Takes a dependency back out. Removing one that was never there
    /// succeeds — the plan ends up the way the caller asked for.</summary>
    public Result RemoveDependency(Guid nodeId, Guid dependsOnId)
    {
        var dependencies = DependenciesOf(nodeId);
        if (dependencies is null) return Result.Failure(RoadmapErrors.NodeNotFound(nodeId));

        dependencies.Remove(dependsOnId);
        return Result.Success();
    }

    private RoadmapItem? FindItem(Guid itemId) => _items.Find(item => item.Id == itemId);

    private Milestone? FindMilestone(Guid milestoneId) => _milestones.Find(milestone => milestone.Id == milestoneId);

    private Dependencies? DependenciesOf(Guid nodeId) =>
        FindItem(nodeId)?.Dependencies ?? FindMilestone(nodeId)?.Dependencies;

    private string TitleOf(Guid nodeId) =>
        FindItem(nodeId)?.Title ?? FindMilestone(nodeId)?.Title ?? nodeId.ToString();

    private void ForgetDependenciesOn(Guid removedId)
    {
        foreach (var dependencies in _items.Select(item => item.Dependencies)
                     .Concat(_milestones.Select(milestone => milestone.Dependencies)))
        {
            dependencies.Remove(removedId);
        }
    }

    private static List<T> Distinct<T>(IEnumerable<T> source, Func<T, Guid> id)
    {
        var seen = new HashSet<Guid>();
        return [.. source.Where(candidate => seen.Add(id(candidate)))];
    }
}
