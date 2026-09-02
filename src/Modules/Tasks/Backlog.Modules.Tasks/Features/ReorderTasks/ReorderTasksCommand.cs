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

        // The index already knows every entry's current rank, so a drag reads one
        // small JSON file and then loads only the entries that actually moved —
        // rather than the whole backlog every time somebody nudges a row.
        var current = (await entries.ListAsync(cancellationToken))
            .ToDictionary(summary => summary.Id, summary => summary.Order);

        for (var index = 0; index < command.IdsInOrder.Count; index++)
        {
            var id = command.IdsInOrder[index];
            if (current.TryGetValue(id, out var order) && order == index) continue;

            var entry = await entries.GetAsync(id, cancellationToken);
            if (entry is null) continue;

            entry.SetOrder(index);
            await entries.SaveAsync(entry, cancellationToken);
        }
    }
}
