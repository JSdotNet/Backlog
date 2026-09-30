using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapCompletedWork"/> from the backlog, through
/// <see cref="ITaskItems"/>: every entry with a completion day on or after the one
/// asked for, and an estimate, with the repositories it was filed under.
/// <para>
/// Every repository's work, not the roadmap's current scope, and each entry says
/// which repositories it belongs to: the global pace counts all of it, and a
/// repository's own pace counts only what was filed under that repository (ADR 0013,
/// ruling 4 as amended on 2026-09-26).
/// </para>
/// <para>
/// A task stores its repositories as the <c>owner/name</c> ids Import resolved them
/// to, and a pace is kept under the alias Settings gives the repository, so each id is
/// turned back into its alias through <see cref="IRepositoryDirectory"/> — the same
/// mapping <see cref="ImportedPlanSource"/> applies to the shelf.
/// </para>
/// <para>
/// The backlog is resolved per call rather than taken in the constructor, because
/// the graph is a loop otherwise: the backlog's plan import hands entries to the
/// roadmap's importer, which asks the pace, which asks this. Constructed eagerly,
/// every scope that builds any of them deadlocks resolving itself. The directory is
/// resolved the same way, so this adapter asks nothing of the container until it
/// is read.
/// </para>
/// </summary>
public sealed class RoadmapCompletedWork : IRoadmapCompletedWork
{
    private readonly Func<ITaskItems> _entries;
    private readonly Func<IRepositoryDirectory> _repositories;

    public RoadmapCompletedWork(Func<ITaskItems> entries, Func<IRepositoryDirectory> repositories)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(repositories);
        _entries = entries;
        _repositories = repositories;
    }

    public IReadOnlyList<string> Repositories =>
        [.. _repositories().Repositories.Select(repository => repository.Alias)];

    public async Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
        DateOnly since,
        CancellationToken cancellationToken = default)
    {
        var backlog = await _entries().ListAsync(cancellationToken).ConfigureAwait(false);
        return Completed(backlog, since, _repositories().Repositories);
    }

    /// <summary>
    /// The selection on its own, over values already read. An id the registry does
    /// not know is kept as the task wrote it, as the shelf keeps it: it matches no
    /// configured repository, so it counts toward the global pace and no other.
    /// </summary>
    internal static IReadOnlyList<CompletedEffortDto> Completed(
        IReadOnlyList<TaskItemDto> backlog,
        DateOnly since,
        IReadOnlyList<TasksRepositoryRef> repositories)
    {
        var aliasById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in repositories) aliasById.TryAdd(repository.Id, repository.Alias);

        return
        [
            .. backlog
                .Where(entry => entry.CompletedOn is { } day && day >= since && entry.Effort is > 0)
                .Select(entry => new CompletedEffortDto(entry.CompletedOn!.Value, entry.Effort!.Value)
                {
                    // Distinct, so a task naming one repository twice counts once.
                    RepositoryAliases =
                    [
                        .. (entry.RepoIds ?? [])
                            .Select(id => aliasById.TryGetValue(id, out var alias) ? alias : id)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                    ]
                })
        ];
    }
}
