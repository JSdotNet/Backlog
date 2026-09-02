using Backlog.Modules.Roadmap.Abstractions;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// What the editor hands back when somebody saves: every field, whether or not they
/// touched it.
/// <para>
/// <paramref name="ItemId"/> is the only thing that says which of the two jobs this
/// is — null plans something new, a value edits what is already there. The band reads
/// it and dispatches the matching use case; the dialog does not know which one it
/// caused, which is what lets one dialog do both.
/// </para>
/// </summary>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
/// <param name="Tag">The slug this item is known by wherever tags are used. The editor
/// pre-fills it from the title-derived slug and lets it be overridden; a blank one
/// means "derive it from the title".</param>
/// <param name="KnowledgeRefs">The knowledge chapters this item points at, as opaque
/// reference strings.</param>
public sealed record RoadmapEditorSubmission(
    Guid? ItemId,
    string Title,
    DateOnly Start,
    DateOnly End,
    PlanningPriority Priority,
    IReadOnlyList<string> RepositoryAliases,
    string? Lane,
    Guid? TaskId,
    string? Notes,
    string? Tag = null,
    IReadOnlyList<string>? KnowledgeRefs = null);

/// <summary>
/// One dependency added or taken away.
/// <para>
/// Separate from <see cref="RoadmapEditorSubmission"/> because it is answered
/// immediately rather than on save: an edge can be refused for a reason none of the
/// fields explain — it would close a cycle — and the person needs to hear that while
/// the tick they just made is still the thing they are looking at.
/// </para>
/// </summary>
/// <param name="NodeId">The thing that waits.</param>
/// <param name="DependsOnId">The thing it waits for.</param>
/// <param name="Added">True to record the dependency, false to take it away.</param>
public sealed record RoadmapDependencyChange(Guid NodeId, Guid DependsOnId, bool Added);

/// <summary>
/// What the milestone editor hands back when somebody saves.
/// <para>
/// <paramref name="MilestoneId"/> null adds a date, a value edits one — the same rule
/// the item editor's submission follows, so the band reads both the same way.
/// </para>
/// </summary>
/// <param name="IsPlanWide">Whether the whole plan is read against this date, and so
/// whether a line is drawn through every band at it.</param>
public sealed record RoadmapMilestoneSubmission(
    Guid? MilestoneId,
    string Title,
    DateOnly On,
    MilestoneKind Kind,
    IReadOnlyList<string> RepositoryAliases,
    bool IsPlanWide);
