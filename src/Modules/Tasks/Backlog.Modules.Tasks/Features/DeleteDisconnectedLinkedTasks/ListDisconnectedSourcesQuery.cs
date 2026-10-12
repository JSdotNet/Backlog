using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.DeleteDisconnectedLinkedTasks;

/// <summary>
/// The sources that left linked tasks behind: every connector-and-target pair with
/// a task on the list and no connected target, enabled or not (ADR 0020, §5).
/// <para>
/// A target is matched the way <see cref="ConnectedTarget.Is"/> matches one —
/// the connector id exactly, the target without regard to case — so two tasks that
/// spell the same repository differently are one source, shown in the spelling of
/// the one changed last.
/// </para>
/// </summary>
public sealed record ListDisconnectedSourcesQuery;

public sealed class ListDisconnectedSourcesQueryHandler(ITaskRepository tasks, IConnectedTargets targets)
    : IQueryHandler<ListDisconnectedSourcesQuery, IReadOnlyList<DisconnectedSource>>
{
    public async Task<IReadOnlyList<DisconnectedSource>> Handle(
        ListDisconnectedSourcesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Every status, archived and done included: the list read hides only
        // tombstones, and a deleted task is not one the person still has.
        var live = await tasks.ListAsync(cancellationToken).ConfigureAwait(false);

        var sources = live
            .Where(task => task.SourceRef is not null)
            .GroupBy(task => task.SourceRef!, PairComparer.Instance)
            .Where(group => targets.Get(group.Key.ConnectorId, group.Key.Target) is null)
            .Select(group =>
            {
                var members = group.Select(task => task.Id).ToHashSet();
                var target = group
                    .OrderByDescending(task => task.UpdatedAt)
                    .ThenBy(task => task.SourceRef!.Target, StringComparer.Ordinal)
                    .First().SourceRef!.Target;

                // A task waits on an id written as text; one that names no live task
                // already blocks, so only the ones naming a task of this source are
                // what the delete would newly leave waiting. A Done or Archived task
                // waits on nothing — the chain never reads its dependencies — so it is
                // not one.
                var dependents = live.Count(task =>
                    task.Status is not (EntryStatus.Done or EntryStatus.Archived)
                    && !members.Contains(task.Id)
                    && task.DependsOn.Any(dependency => Guid.TryParse(dependency, out var id) && members.Contains(id)));

                return new DisconnectedSource(group.Key.ConnectorId, target, members.Count, dependents);
            })
            .OrderBy(source => source.ConnectorId, StringComparer.Ordinal)
            .ThenBy(source => source.Target, StringComparer.OrdinalIgnoreCase);

        return [.. sources];
    }

    /// <summary>A pair as <see cref="ConnectedTarget.Is"/> compares one: the
    /// connector id exactly, the target without regard to case.</summary>
    private sealed class PairComparer : IEqualityComparer<SourceRef>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals(SourceRef? x, SourceRef? y) =>
            ReferenceEquals(x, y)
            || (x is not null && y is not null
                && string.Equals(x.ConnectorId, y.ConnectorId, StringComparison.Ordinal)
                && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase));

        public int GetHashCode(SourceRef obj) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.ConnectorId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target));
    }
}
