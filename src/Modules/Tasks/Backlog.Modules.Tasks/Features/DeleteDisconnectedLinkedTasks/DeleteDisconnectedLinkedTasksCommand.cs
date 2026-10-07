using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.DeleteDisconnectedLinkedTasks;

/// <summary>
/// Deletes every task a disconnected source brought in, whatever its status — the
/// settings page's "Delete tasks", behind a confirmation the person has given.
/// <para>
/// A tombstone per task, as a single delete is (<c>DeleteTaskCommand</c>), and for
/// the same reason: a row simply gone would come back from another machine. The
/// tombstone also keeps the sync from making the task again if the source is
/// connected later. Nothing is written back to the source, and the tasks that wait
/// on a deleted one are left as they are, the way a single delete leaves them.
/// </para>
/// </summary>
public sealed record DeleteDisconnectedLinkedTasksCommand(string ConnectorId, string Target)
{
    public const string StillConnectedCode = "linked_tasks.target_connected";

    public static readonly Error BlankPair = Error.Validation(
        "linked_tasks.delete_blank_pair",
        "Both the connector and the repository or product are needed.");

    /// <summary>The pair was connected again between the list and the confirmation:
    /// its tasks are the sync's again, so deleting them is no longer what was
    /// confirmed.</summary>
    public static Error StillConnected(string target) => Error.Conflict(
        StillConnectedCode,
        $"{target} is connected again, so its tasks were not deleted.");
}

/// <summary>Runs the delete and answers how many tasks it tombstoned, for the
/// settings page's status line.</summary>
public sealed class DeleteDisconnectedLinkedTasksCommandHandler(ITaskRepository tasks, IConnectedTargets targets)
    : ICommandHandler<DeleteDisconnectedLinkedTasksCommand, Result<int>>
{
    public async Task<Result<int>> Handle(DeleteDisconnectedLinkedTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ConnectorId) || string.IsNullOrWhiteSpace(command.Target))
        {
            return DeleteDisconnectedLinkedTasksCommand.BlankPair;
        }

        if (targets.Get(command.ConnectorId, command.Target) is not null)
        {
            return DeleteDisconnectedLinkedTasksCommand.StillConnected(command.Target);
        }

        // Snapshotted before the loop saves into the store it read from.
        var stored = (await tasks.ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var deleted = 0;

        foreach (var task in stored)
        {
            if (task.SourceRef is not { } source
                || !string.Equals(source.ConnectorId, command.ConnectorId, StringComparison.Ordinal)
                || !string.Equals(source.Target, command.Target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            task.MarkDeleted();
            await tasks.SaveAsync(task, cancellationToken).ConfigureAwait(false);
            deleted++;
        }

        return deleted;
    }
}
