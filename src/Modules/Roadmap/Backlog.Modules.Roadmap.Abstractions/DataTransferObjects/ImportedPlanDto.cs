namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// One plan the backlog carries because it was imported, as the roadmap needs to
/// know it before it is placed.
/// </summary>
/// <param name="Tag">The plan tag, bare — the sigil lifted, the slug a Roadmap Item
/// carrying it would hold.</param>
/// <param name="RepositoryAliases">The repositories its tasks are filed against,
/// by the alias the plan files an item under. A repository the registry does not
/// know stays as the tasks wrote it.</param>
/// <param name="TaskCount">How many tasks carry the plan's id.</param>
/// <param name="TotalEffort">The sum of the points those tasks registered.</param>
/// <param name="UnestimatedCount">How many of them registered none.</param>
/// <param name="RepositoryParts">The same figures per repository, in the order of
/// <paramref name="RepositoryAliases"/>. A task filed against two repositories counts
/// in both, so the parts can add up to more than the plan; a task filed against none
/// is in no part.</param>
public sealed record ImportedPlanDto(
    string Tag,
    IReadOnlyList<string> RepositoryAliases,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount,
    IReadOnlyList<ImportedPlanPartDto>? RepositoryParts = null)
{
    /// <summary>The per-repository figures, never null.</summary>
    public IReadOnlyList<ImportedPlanPartDto> Parts => RepositoryParts ?? [];

    /// <summary>The effort as the import command takes it, so placing this plan from
    /// the shelf divides the same numbers an import would.</summary>
    public PlanTagEffortDto Effort => new(Tag, TotalEffort, UnestimatedCount);
}

/// <summary>One repository's share of an imported plan.</summary>
/// <param name="RepositoryAlias">The alias, as in <see cref="ImportedPlanDto.RepositoryAliases"/>.</param>
/// <param name="TaskCount">How many of the plan's tasks are filed against it.</param>
/// <param name="TotalEffort">The points those tasks registered.</param>
/// <param name="UnestimatedCount">How many of them registered none.</param>
public sealed record ImportedPlanPartDto(
    string RepositoryAlias,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount);
