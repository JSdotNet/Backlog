using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapCompletedWork"/> from the backlog, through
/// <see cref="ITaskItems"/>: every entry with a completion day on or after the one
/// asked for, and an estimate.
/// <para>
/// Every repository's work, not the roadmap's current scope: a pace is the reader's,
/// and the stretch they measured it over went on whatever they were working on.
/// </para>
/// <para>
/// The backlog is resolved per call rather than taken in the constructor, because
/// the graph is a loop otherwise: the backlog's plan import hands entries to the
/// roadmap's importer, which asks the pace, which asks this. Constructed eagerly,
/// every scope that builds any of them deadlocks resolving itself.
/// </para>
/// </summary>
public sealed class RoadmapCompletedWork : IRoadmapCompletedWork
{
    private readonly Func<ITaskItems> _entries;

    public RoadmapCompletedWork(Func<ITaskItems> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;
    }

    public async Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
        DateOnly since,
        CancellationToken cancellationToken = default)
    {
        var backlog = await _entries().ListAsync(cancellationToken);
        return Completed(backlog, since);
    }

    internal static IReadOnlyList<CompletedEffortDto> Completed(IReadOnlyList<TaskItemDto> backlog, DateOnly since) =>
    [
        .. backlog
            .Where(entry => entry.CompletedOn is { } day && day >= since && entry.Effort is > 0)
            .Select(entry => new CompletedEffortDto(entry.CompletedOn!.Value, entry.Effort!.Value))
    ];
}
