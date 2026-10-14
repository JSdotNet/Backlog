using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="ISprintTaskSource"/>, answered from Tasks' published surface
/// alone: <see cref="ITaskItems"/> for the tasks and <see cref="IRepositoryDirectory"/>
/// for the aliases their repository ids are known by. It reads and never writes — a
/// sprint never changes a task (local ADR 0028).
/// <para>
/// Both ports are resolved per call, for the reason the roadmap's adapters give: they
/// are scoped, and taking them in the constructor would tie this adapter to whichever
/// scope built it.
/// </para>
/// </summary>
public sealed class SprintTaskSource(Func<ITaskItems> tasks, Func<IRepositoryDirectory?> repositories) : ISprintTaskSource
{
    public async Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var backlog = await tasks().ListAsync(cancellationToken).ConfigureAwait(false);
        return Select(backlog, query, repositories()?.Repositories ?? []);
    }

    /// <summary>The tasks <paramref name="query"/> asks for, in Progress's words.</summary>
    internal static IReadOnlyList<SprintTaskDto> Select(
        IReadOnlyList<TaskItemDto> backlog,
        SprintTaskQuery query,
        IReadOnlyList<TasksRepositoryRef> registry)
    {
        var aliasById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in registry) aliasById.TryAdd(repository.Id, repository.Alias);

        var ids = query.TaskIds.ToHashSet();
        var plans = query.PlanTags.Select(Bare).Where(tag => tag.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repos = query.RepositoryAliases.Select(alias => alias.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. backlog
                .Select(entry => ToSprintTask(entry, aliasById))
                .Where(task =>
                    ids.Contains(task.Id)
                    || (task.PlanTags.Any(plans.Contains) && task.RepositoryAliases.Any(repos.Contains)))
                .DistinctBy(task => task.Id)
        ];
    }

    internal static SprintTaskDto ToSprintTask(TaskItemDto entry, IReadOnlyDictionary<string, string> aliasById) =>
        new(
            entry.Id,
            entry.Title,
            entry.Status switch
            {
                EntryStatus.Draft => SprintTaskStatus.Draft,
                EntryStatus.Ready => SprintTaskStatus.Ready,
                EntryStatus.InProgress => SprintTaskStatus.InProgress,
                EntryStatus.Done => SprintTaskStatus.Done,
                _ => SprintTaskStatus.Archived
            },
            entry.BlockedSince is not null,
            [.. entry.Tags.Where(tag => tag.TrimStart().StartsWith('+')).Select(Bare).Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. (entry.RepoIds ?? [])
                .Select(id => aliasById.TryGetValue(id, out var alias) ? alias : id)
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. entry.Tags],
            entry.CompletedOn,
            entry.CreatedAt is { } created ? DateOnly.FromDateTime(created.LocalDateTime) : null,
            [.. entry.Projections
                .Where(projection => projection.TargetType == EntryProjectionDto.PullRequestTargetType)
                .Select(projection => new PullRequestRefDto(projection.RepoId, projection.ExternalId))
                .Distinct()]);

    private static string Bare(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }
}
