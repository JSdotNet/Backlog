using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.RestoreDeletedLinkedTasks;

/// <summary>
/// The sources with deleted linked tasks: every connector-and-target pair a
/// tombstoned task names, with how many it names (ADR 0020, §5). What the settings
/// page reads to offer Restore deleted tasks on a connected target's card only when
/// there is something to restore.
/// <para>
/// A target is matched the way <see cref="ConnectedTarget.Is"/> matches one — the
/// connector id exactly, the target without regard to case — and shown in the
/// spelling of the task deleted last, as <c>ListDisconnectedSourcesQuery</c> shows
/// a disconnected source. Connected or not: the page matches the pairs to its cards.
/// </para>
/// </summary>
public sealed record ListDeletedLinkedSourcesQuery;

public sealed class ListDeletedLinkedSourcesQueryHandler(ITaskRepository tasks)
    : IQueryHandler<ListDeletedLinkedSourcesQuery, IReadOnlyList<DeletedLinkedSource>>
{
    public async Task<IReadOnlyList<DeletedLinkedSource>> Handle(
        ListDeletedLinkedSourcesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var deleted = await tasks.ListDeletedAsync(cancellationToken).ConfigureAwait(false);

        var sources = deleted
            .Where(task => task.SourceRef is not null)
            .GroupBy(task => (task.SourceRef!.ConnectorId, task.SourceRef.Target), PairComparer.Instance)
            .Select(group => new DeletedLinkedSource(
                group.Key.ConnectorId,
                group
                    .OrderByDescending(task => task.UpdatedAt)
                    .ThenBy(task => task.SourceRef!.Target, StringComparer.Ordinal)
                    .First().SourceRef!.Target,
                group.Count()))
            .OrderBy(source => source.ConnectorId, StringComparer.Ordinal)
            .ThenBy(source => source.Target, StringComparer.OrdinalIgnoreCase);

        return [.. sources];
    }

    /// <summary>A pair as <see cref="ConnectedTarget.Is"/> compares one: the
    /// connector id exactly, the target without regard to case.</summary>
    private sealed class PairComparer : IEqualityComparer<(string ConnectorId, string Target)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((string ConnectorId, string Target) x, (string ConnectorId, string Target) y) =>
            string.Equals(x.ConnectorId, y.ConnectorId, StringComparison.Ordinal)
            && string.Equals(x.Target, y.Target, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ConnectorId, string Target) obj) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.ConnectorId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Target));
    }
}
