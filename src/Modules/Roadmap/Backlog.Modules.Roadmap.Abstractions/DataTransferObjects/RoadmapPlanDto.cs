namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// The whole plan as a caller receives it: everything planned, everything fixed,
/// and everywhere the plan currently disagrees with itself.
/// <para>
/// One DTO for the lot rather than a list per concept, because a plan is only
/// meaningful whole — a dependency arrow needs both of its ends, and a
/// contradiction names a pair. Handing these back separately would let a caller
/// draw half a plan and believe it.
/// </para>
/// </summary>
public sealed record RoadmapPlanDto(
    IReadOnlyList<RoadmapItemDto> Items,
    IReadOnlyList<RoadmapMilestoneDto> Milestones,
    IReadOnlyList<PlanContradictionDto> Contradictions,
    IReadOnlyDictionary<string, int>? BandColours = null)
{
    public static RoadmapPlanDto Empty { get; } = new([], [], []);

    /// <summary>Which of the sanctioned band colours each repository has been given,
    /// keyed by alias. A repository absent from this has not been chosen for; the view
    /// places it. Never a colour value — only which of the approved set.</summary>
    public IReadOnlyDictionary<string, int> Bands =>
        BandColours ?? new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Whether there is anything at all to draw.</summary>
    public bool IsEmpty => Items.Count == 0 && Milestones.Count == 0;
}

/// <summary>
/// One piece of planned work.
/// </summary>
/// <param name="Id">Stable across every reschedule, so a dependency or a foreign
/// id keeps pointing at the same planned work.</param>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, <em>inclusive</em> — "through the 31st" means the
/// 31st. A caller that treats this as exclusive is one day short on every item.</param>
/// <param name="Priority">The plan's own priority. Never a linked entry's.</param>
/// <param name="RepositoryAliases">The repositories this belongs to, as written
/// and unresolved. Empty means unfiled, not "all".</param>
/// <param name="Lane">The row it is filed under within its repository band. Null
/// means the default lane.</param>
/// <param name="TaskId">The entry that executes this item, when there is
/// one. May dangle: an entry can be deleted while the plan still intends the
/// work.</param>
/// <param name="DependsOn">The nodes — items or milestones — this waits for.</param>
/// <param name="Tag">The slug this item is known by wherever tags are used — the
/// backlog item tag list, and a <c>roadmap:</c> field in a knowledge chapter's
/// <c>meta</c> block. Always present: derived from the title when the item was first
/// given none, and left alone by a rename.</param>
/// <param name="KnowledgeRefs">The knowledge chapters this item points at, as opaque
/// references that may dangle. Empty means it points at none.</param>
public sealed record RoadmapItemDto(
    Guid Id,
    string Title,
    DateOnly Start,
    DateOnly End,
    PlanningPriority Priority,
    IReadOnlyList<string> RepositoryAliases,
    string? Lane,
    Guid? TaskId,
    IReadOnlyList<Guid> DependsOn,
    string? Notes = null,
    string Tag = "",
    IReadOnlyList<string>? KnowledgeRefs = null)
{
    /// <summary>How many days it covers, counting both ends.</summary>
    public int Days => End.DayNumber - Start.DayNumber + 1;

    /// <summary>The knowledge chapters this item points at, never null.</summary>
    public IReadOnlyList<string> Knowledge => KnowledgeRefs ?? [];
}

/// <summary>
/// A single day the plan is read against. Not a one-day <see cref="RoadmapItemDto"/>:
/// it has no duration, and it is rescheduled by moving one date.
/// </summary>
/// <param name="IsPlanWide">Whether the whole plan is read against this date, rather
/// than one band — drawn as a line through every row.</param>
public sealed record RoadmapMilestoneDto(
    Guid Id,
    string Title,
    DateOnly On,
    MilestoneKind Kind,
    IReadOnlyList<string> RepositoryAliases,
    string? Lane,
    IReadOnlyList<Guid> DependsOn,
    bool IsPlanWide = false);

/// <summary>
/// Somewhere the plan disagrees with itself about dates — work opening before the
/// thing it waits on has closed.
/// <para>
/// Reported, never corrected. Discovering that a date does not fit is the point of
/// drawing a plan, and quietly shifting the dependent work would hide it. This is
/// not a cycle: a cycle is refused outright and never reaches a caller.
/// </para>
/// </summary>
/// <param name="NodeId">The node that waits.</param>
/// <param name="DependsOnId">The node it waits for.</param>
/// <param name="Reason">Why the two do not fit, in words that can be shown.</param>
public sealed record PlanContradictionDto(Guid NodeId, Guid DependsOnId, string Reason);
