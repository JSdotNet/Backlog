namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// What Import hands the roadmap in one run (ADR 0013, ruling 3): the document's
/// <c>plan</c> entries, the ones Import made up for "Lay out on the roadmap", and
/// the effort now gathered under every plan tag either kind names.
/// </summary>
/// <param name="Entries">The document's own <c>plan</c> entries, in document
/// order.</param>
/// <param name="LayOutIfMissing">One made-up entry per plan tag of a document that
/// wrote no <c>plan</c> entry, laid out only where no item carries the tag yet.</param>
/// <param name="GatheredEffort">Per plan tag, what the stored tasks under it
/// register — the ones this import just wrote and the ones already there.</param>
public sealed record RoadmapPlanIntakeRequestDto(
    IReadOnlyList<RoadmapPlanEntryDto> Entries,
    IReadOnlyList<RoadmapPlanEntryDto> LayOutIfMissing,
    IReadOnlyList<RoadmapPlanEffortDto> GatheredEffort);

/// <summary>
/// One <c>plan</c> entry, as plain values.
/// </summary>
/// <param name="Title">The heading, as written.</param>
/// <param name="Tag">The one <c>+</c> plan tag, sigil and all; the adapter lifts
/// it.</param>
/// <param name="LocalId">The entry's <c>id:</c>; the roadmap defaults it to the
/// tag.</param>
/// <param name="RepositoryAliases">The <c>repo:</c> values, as written or as the
/// reader matched them — never registered.</param>
/// <param name="Priority">The <c>*priority</c>, or null for medium.</param>
/// <param name="Due">The <c>due:</c>, the last day inclusive.</param>
/// <param name="After">The plan-level <c>after:</c> values.</param>
/// <param name="Notes">The body, verbatim, or null when it is empty.</param>
public sealed record RoadmapPlanEntryDto(
    string Title,
    string Tag,
    string? LocalId,
    IReadOnlyList<string> RepositoryAliases,
    Priority? Priority,
    DateOnly? Due,
    IReadOnlyList<string> After,
    string? Notes);

/// <summary>What the stored tasks under one plan tag register: the sum of their
/// points, and how many registered none.</summary>
public sealed record RoadmapPlanEffortDto(string Tag, int TotalEffort, int UnestimatedCount);

/// <summary>An <c>after:</c> value that named nothing at its entry's level, dropped
/// rather than stored.</summary>
/// <param name="Entry">The entry that wrote it — its <c>id:</c> or tag, else its
/// title.</param>
/// <param name="After">The value, as written.</param>
public sealed record ImportUnresolvedDependencyDto(string Entry, string After);

/// <summary>
/// The roadmap half of an import.
/// </summary>
/// <param name="Created">Roadmap items the import added.</param>
/// <param name="Updated">Items a <c>plan</c> entry matched by tag and revised.</param>
/// <param name="Relengthened">Items only the gathered effort touched: still
/// effort-placed, so the end moved and the start stayed.</param>
/// <param name="Skipped">Titles of <c>plan</c> entries that named no <c>+</c> tag, or
/// more than one — the tag is the one thing a later import finds the item by, so none
/// is guessed.</param>
/// <param name="EffortIgnored">Titles of <c>plan</c> entries that wrote
/// <c>effort:</c>, which an item does not honour: its size is what it gathers.</param>
/// <param name="AmbiguousTags">Tags more than one existing item carries; the first by
/// creation order was the one updated.</param>
/// <param name="UnresolvedDependencies">Plan-level <c>after:</c> values that named no
/// plan.</param>
/// <param name="Refusal">Why the roadmap took none of it — a cycle, which refuses the
/// whole batch — or null. The tasks are imported either way.</param>
public sealed record RoadmapIntakeResultDto(
    int Created,
    int Updated,
    int Relengthened,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> EffortIgnored,
    IReadOnlyList<string> AmbiguousTags,
    IReadOnlyList<ImportUnresolvedDependencyDto> UnresolvedDependencies,
    string? Refusal = null)
{
    /// <summary>Nothing crossed and nothing was said about it.</summary>
    public static RoadmapIntakeResultDto Empty { get; } = new(0, 0, 0, [], [], [], []);

    /// <summary>Refused whole: nothing was laid out.</summary>
    public static RoadmapIntakeResultDto Refused(string reason) => Empty with { Refusal = reason };
}
