using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.ReorderTasks;

/// <summary>Writes a new hand-made ranking: position in the list becomes the
/// entry's order.</summary>
public sealed record ReorderTasksCommand(IReadOnlyList<Guid> IdsInOrder);

public sealed class ReorderTasksCommandHandler(ITaskRepository entries)
    : ICommandHandler<ReorderTasksCommand>
{
    public async Task Handle(ReorderTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The list is whole aggregates, so it is all a drag needs to read: the
        // entries that moved are re-ranked from it and written back together, as
        // one write, rather than fetched again one by one. An id the list does
        // not have — gone, or tombstoned — is skipped. Each position is compared
        // with the rank as it was read, not as this loop has left it, so an id
        // sent twice ends where it did before the list was read whole.
        var current = (await entries.ListAsync(cancellationToken)).ToDictionary(entry => entry.Id);
        var rankAsRead = current.ToDictionary(pair => pair.Key, pair => pair.Value.Order);
        var moved = new List<TaskItem>();

        for (var index = 0; index < command.IdsInOrder.Count; index++)
        {
            var id = command.IdsInOrder[index];
            if (!current.TryGetValue(id, out var entry) || rankAsRead[id] == index) continue;

            entry.SetOrder(index);
            if (!moved.Contains(entry)) moved.Add(entry);
        }

        if (moved.Count > 0) await entries.SaveManyAsync(moved, cancellationToken);
    }
}
