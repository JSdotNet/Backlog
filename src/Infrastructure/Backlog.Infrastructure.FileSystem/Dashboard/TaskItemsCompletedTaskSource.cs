using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using TasksRepositoryDirectory = Backlog.Modules.Tasks.Abstractions.Services.IRepositoryDirectory;

namespace Backlog.Infrastructure.FileSystem.Dashboard;

/// <summary>
/// ADAPTER — answers the dashboard's <see cref="ICompletedTaskSource"/> from the Tasks
/// context's backlog.
/// <para>
/// A task's repositories are stored as <c>owner/name</c> ids and the dashboard's scope
/// holds aliases, so each id is resolved through the registry here. An id the registry
/// does not know crosses as itself, on <c>ImportedPlanSource</c>'s precedent: a legacy
/// alias-shaped value then still matches its chip, and an unknown id matches none.
/// </para>
/// </summary>
public sealed class TaskItemsCompletedTaskSource(ITaskItems entries, TasksRepositoryDirectory repositories)
    : ICompletedTaskSource
{
    public async Task<IReadOnlyList<CompletedTask>> GetCompletedAsync(
        DateOnly since,
        CancellationToken cancellationToken = default)
    {
        var backlog = await entries.ListAsync(cancellationToken);
        return Completed(backlog, since, repositories);
    }

    internal static IReadOnlyList<CompletedTask> Completed(
        IReadOnlyList<TaskItemDto> backlog,
        DateOnly since,
        TasksRepositoryDirectory repositories) =>
    [
        .. backlog
            .Where(entry => entry.CompletedOn is { } day && day >= since)
            .Select(entry => new CompletedTask(
                entry.CompletedOn!.Value,
                entry.Effort,
                [
                    .. (entry.RepoIds ?? [])
                        .Select(id => repositories.Resolve(id)?.Alias ?? id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                ]))
    ];
}
