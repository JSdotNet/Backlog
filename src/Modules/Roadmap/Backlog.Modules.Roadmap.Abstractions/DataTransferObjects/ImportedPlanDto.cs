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
public sealed record ImportedPlanDto(
    string Tag,
    IReadOnlyList<string> RepositoryAliases,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount)
{
    /// <summary>The effort as the import command takes it, so placing this plan from
    /// the shelf divides the same numbers an import would.</summary>
    public PlanTagEffortDto Effort => new(Tag, TotalEffort, UnestimatedCount);
}
