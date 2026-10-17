using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="IProgressTaskSource"/>, answered from Tasks' published surface
/// alone: <see cref="ITaskItems"/> for the tasks and <see cref="IRepositoryDirectory"/>
/// for the aliases their repository ids are known by. It reads and never writes —
/// Progress never changes a task (local ADR 0035).
/// <para>
/// Both ports are resolved per call, for the reason the roadmap's adapters give: they
/// are scoped, and taking them in the constructor would tie this adapter to whichever
/// scope built it.
/// </para>
/// </summary>
public sealed class ProgressTaskSource(Func<ITaskItems> tasks, Func<IRepositoryDirectory?> repositories) : IProgressTaskSource
{
    public async Task<IReadOnlyList<ProgressTaskDto>> ReadAsync(ProgressTaskQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var backlog = await tasks().ListAsync(cancellationToken).ConfigureAwait(false);
        return Select(backlog, query, repositories()?.Repositories ?? []);
    }

    /// <summary>The tasks <paramref name="query"/> asks for, in Progress's words.</summary>
    internal static IReadOnlyList<ProgressTaskDto> Select(
        IReadOnlyList<TaskItemDto> backlog,
        ProgressTaskQuery query,
        IReadOnlyList<TasksRepositoryRef> registry)
    {
        var aliasById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in registry) aliasById.TryAdd(repository.Id, repository.Alias);

        var ids = query.TaskIds.ToHashSet();
        var plans = query.PlanTags.Select(Bare).Where(tag => tag.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repos = query.RepositoryAliases.Select(alias => alias.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selected = backlog
            .Select(entry => (Entry: entry, Task: ToProgressTask(entry, aliasById)))
            .Where(pair =>
                ids.Contains(pair.Task.Id)
                || (pair.Task.PlanTags.Any(plans.Contains) && (query.AnyRepository || pair.Task.RepositoryAliases.Any(repos.Contains)))
                || query.CompletedInRange(pair.Task.CompletedOn)
                || query.DueInRange(pair.Task.DueOn))
            .DistinctBy(pair => pair.Task.Id)
            .ToList();

        if (!selected.Any(pair => pair.Entry.DependsOn is { Count: > 0 })) return [.. selected.Select(pair => pair.Task)];

        // Dependencies resolve against the whole backlog, as the pane and the roadmap
        // resolve them, so a slug the import left behind still finds its step.
        var index = new DependencyResolution.CandidateIndex(
            [.. backlog.Select(entry => new DependencyResolution.Candidate(entry.Id, entry.ImportItemId, entry.ImportPlanId))]);
        var byId = new Dictionary<Guid, TaskItemDto>();
        foreach (var entry in backlog) byId.TryAdd(entry.Id, entry);

        return [.. selected.Select(pair => pair.Task with { WaitsOnOpenWork = WaitsOnOpenWork(pair.Entry, index, byId) })];
    }

    /// <summary>Whether one of <paramref name="entry"/>'s dependencies names a task that is
    /// not done — neither Done nor completed — or names no live task at all.</summary>
    private static bool WaitsOnOpenWork(
        TaskItemDto entry,
        DependencyResolution.CandidateIndex index,
        Dictionary<Guid, TaskItemDto> byId)
    {
        if (entry.DependsOn is not { Count: > 0 } declared) return false;

        return DependencyResolution.ResolveAll(declared, index, entry.ImportPlanId, entry.Tags)
            .Any(value => !Guid.TryParse(value, out var id)
                || !byId.TryGetValue(id, out var predecessor)
                || (predecessor.Status != EntryStatus.Done && predecessor.CompletedOn is null));
    }

    internal static ProgressTaskDto ToProgressTask(TaskItemDto entry, IReadOnlyDictionary<string, string> aliasById) =>
        new(
            entry.Id,
            entry.Title,
            entry.Status switch
            {
                EntryStatus.Draft => ProgressTaskStatus.Draft,
                EntryStatus.Ready => ProgressTaskStatus.Ready,
                EntryStatus.InProgress => ProgressTaskStatus.InProgress,
                EntryStatus.Done => ProgressTaskStatus.Done,
                _ => ProgressTaskStatus.Archived
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
                .Distinct()],
            entry.StartedOn,
            entry.BlockedSince,
            entry.DueOn,
            entry.Effort,
            entry.Order);

    private static string Bare(string tag)
    {
        var trimmed = tag.Trim();
        return trimmed.StartsWith('+') ? trimmed[1..].Trim() : trimmed;
    }
}
