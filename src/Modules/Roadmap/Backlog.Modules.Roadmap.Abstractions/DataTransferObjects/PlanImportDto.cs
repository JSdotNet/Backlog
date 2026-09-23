namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// One plan-level entry of an imported document, already parsed — what a <c>plan</c>
/// entry says about the Roadmap Item it stands for (ADR 0013, ruling 2).
/// <para>
/// Plain values and nothing of the parser's. The entry text grammar belongs to Tasks,
/// and this module may not reference it (<c>ModuleBoundaryTests</c>), so whoever read
/// the document hands its facts across in Roadmap's own shape.
/// </para>
/// </summary>
/// <param name="Title">The entry's heading, as written.</param>
/// <param name="Tag">The plan's tag, bare — the sigil is lifted by whoever calls,
/// at the boundary. Null or blank means the entry wrote none; it is reported and
/// skipped, because the tag is the one thing a later import finds the item by.</param>
/// <param name="LocalId">The entry's <c>id:</c>, which sibling entries' <c>after:</c>
/// name it by. Defaults to the tag when absent.</param>
/// <param name="RepositoryAliases">The entry's <c>repo:</c> values, as written.</param>
/// <param name="Due">The entry's <c>due:</c>, the last day inclusive, when it wrote
/// one.</param>
/// <param name="After">The entry's <c>after:</c> values: a sibling's local id, the tag
/// of an item already on the plan, or a node id.</param>
/// <param name="Notes">The entry's body, verbatim.</param>
public sealed record PlanImportEntryDto(
    string Title,
    string? Tag,
    string? LocalId = null,
    IReadOnlyList<string>? RepositoryAliases = null,
    PlanningPriority Priority = PlanningPriority.Medium,
    DateOnly? Due = null,
    IReadOnlyList<string>? After = null,
    string? Notes = null);

/// <summary>
/// What the caller already knows about the work gathered under one plan tag: the
/// story points its tasks registered, and how many registered none.
/// <para>
/// Handed in rather than read, because the effort is Tasks' and the caller has just
/// written it. An item placed before its tasks landed would be placed against the
/// previous version's effort.
/// </para>
/// </summary>
/// <param name="Tag">The plan tag, bare.</param>
/// <param name="TotalEffort">The sum of the points the gathered tasks registered.</param>
/// <param name="UnestimatedCount">How many gathered tasks registered no estimate.</param>
public sealed record PlanTagEffortDto(string Tag, int TotalEffort, int UnestimatedCount);

/// <summary>
/// What an import did to the plan, entry by entry — including what it declined to do.
/// </summary>
/// <param name="Created">Items the import added, as they now stand.</param>
/// <param name="Updated">Items the import matched by tag and revised.</param>
/// <param name="SkippedWithoutTag">The titles of entries that named no tag.</param>
/// <param name="AmbiguousTags">Tags that more than one existing item carries.</param>
/// <param name="UnresolvedDependencies">The <c>after:</c> values that named nothing,
/// dropped rather than stored.</param>
/// <param name="Scheduled">Every window the import set or moved, in the
/// <c>RoadmapItemScheduled</c> shape.</param>
/// <param name="Relengthened">Items no entry named whose effort-placed window the
/// gathered effort re-lengthened — the start kept, the end recomputed.</param>
public sealed record PlanImportResultDto(
    IReadOnlyList<RoadmapItemDto> Created,
    IReadOnlyList<RoadmapItemDto> Updated,
    IReadOnlyList<string> SkippedWithoutTag,
    IReadOnlyList<AmbiguousPlanTagDto> AmbiguousTags,
    IReadOnlyList<UnresolvedPlanDependencyDto> UnresolvedDependencies,
    IReadOnlyList<RoadmapItemScheduledDto> Scheduled,
    IReadOnlyList<RoadmapItemDto> Relengthened);

/// <summary>
/// A tag more than one existing item carries. The first of them by creation order was
/// updated; the rest were left alone, and are named so a person can decide.
/// </summary>
public sealed record AmbiguousPlanTagDto(string Tag, Guid UpdatedItemId, IReadOnlyList<Guid> OtherItemIds);

/// <summary>An <c>after:</c> value on the entry tagged <paramref name="Tag"/> that
/// resolved to nothing on the plan or in the document.</summary>
public sealed record UnresolvedPlanDependencyDto(string Tag, string After);

/// <summary>
/// A Roadmap Item's Planned Window was set or changed — the payload of the
/// <c>RoadmapItemScheduled</c> published language
/// (<c>.domain/roadmap/domain.md#roadmapitemscheduled</c>).
/// </summary>
/// <param name="Start">The new window's first day, inclusive.</param>
/// <param name="End">The new window's last day, <em>inclusive</em>.</param>
/// <param name="PreviousStart">The replaced window's first day; null when the item is
/// newly planned — which is how a consumer tells the two apart. It never means
/// "unchanged".</param>
/// <param name="PreviousEnd">The replaced window's last day; null when newly
/// planned.</param>
public sealed record RoadmapItemScheduledDto(
    Guid RoadmapItemId,
    string Title,
    DateOnly Start,
    DateOnly End,
    DateOnly? PreviousStart,
    DateOnly? PreviousEnd,
    PlanningPriority PlanningPriority,
    IReadOnlyList<string> RepositoryAliases,
    Guid? TaskId);
