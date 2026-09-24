using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IImportedPlanSource"/> from the backlog, through
/// <see cref="ITaskItems"/>: every task carrying an <c>import_plan_id</c>, grouped
/// by it and totalled.
/// <para>
/// The same arrangement as <see cref="RoadmapItemRollupService"/> — the band asks
/// its own port, and this adapter, which may see both contexts, does the reading.
/// It counts and sums what the tasks registered and estimates nothing.
/// </para>
/// </summary>
public sealed class ImportedPlanSource : IImportedPlanSource
{
    /// <summary>The sigil a plan tag is written with. Import identifies a plan by the
    /// tag written with it, so an id without it is a legacy plan's bare tag.</summary>
    private const char PlanSigil = '+';

    private readonly ITaskItems _entries;
    private readonly IRepositoryDirectory _repositories;

    public ImportedPlanSource(ITaskItems entries, IRepositoryDirectory repositories)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(repositories);
        _entries = entries;
        _repositories = repositories;
    }

    public async Task<IReadOnlyList<ImportedPlanDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var backlog = await _entries.ListAsync(cancellationToken);
        return Summarise(backlog, _repositories.Repositories);
    }

    /// <summary>
    /// The grouping on its own, over values already read.
    /// <para>
    /// A task stores its repositories as the <c>owner/name</c> ids Import resolved
    /// them to, and a roadmap item files under the alias Settings gives the
    /// repository — so each id is turned back into its alias. An id the registry
    /// does not know is kept as the task wrote it: Import stores an unresolved
    /// <c>repo:</c> name verbatim, and that name is the nearest thing to an alias
    /// there is.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<ImportedPlanDto> Summarise(
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyList<TasksRepositoryRef> repositories)
    {
        var aliasById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in repositories) aliasById.TryAdd(repository.Id, repository.Alias);

        var plans = new List<ImportedPlanDto>();

        // Ordinal on the id, as Import matches it: the plan id is exactly what the
        // import wrote. First appearance fixes the order, so the shelf does not
        // reshuffle between two reads of an unchanged backlog.
        foreach (var group in backlog
                     .Where(entry => entry.ImportPlanId is { Length: > 1 } id && id[0] == PlanSigil)
                     .GroupBy(entry => entry.ImportPlanId!, StringComparer.Ordinal))
        {
            var aliases = new List<string>();
            var tasksByAlias = new Dictionary<string, List<TaskItemDto>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in group)
            {
                // Distinct per task, so a task naming one repository twice counts once.
                var entryAliases = (entry.RepoIds ?? [])
                    .Select(id => aliasById.TryGetValue(id, out var known) ? known : id)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var alias in entryAliases)
                {
                    if (!tasksByAlias.TryGetValue(alias, out var tasks))
                    {
                        aliases.Add(alias);
                        tasksByAlias[alias] = tasks = [];
                    }

                    tasks.Add(entry);
                }
            }

            plans.Add(new ImportedPlanDto(
                Tag: group.Key[1..],
                RepositoryAliases: aliases,
                TaskCount: group.Count(),
                TotalEffort: group.Sum(entry => entry.Effort ?? 0),
                UnestimatedCount: group.Count(entry => entry.Effort is null),
                RepositoryParts:
                [
                    .. aliases.Select(alias => new ImportedPlanPartDto(
                        alias,
                        tasksByAlias[alias].Count,
                        tasksByAlias[alias].Sum(entry => entry.Effort ?? 0),
                        tasksByAlias[alias].Count(entry => entry.Effort is null)))
                ]));
        }

        return plans;
    }
}
