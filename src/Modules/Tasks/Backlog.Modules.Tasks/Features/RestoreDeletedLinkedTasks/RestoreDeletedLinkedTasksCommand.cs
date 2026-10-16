using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.RestoreDeletedLinkedTasks;

/// <summary>
/// Brings back every deleted task a connected target brought in — the settings
/// page's "Restore deleted tasks", behind a confirmation the person has given.
/// <para>
/// Every one, however it went: deleted with its source, which the sync would bring
/// back by itself only while the item is still among what it fetches, and deleted
/// on its own, which the sync never brings back — the confirmation says so. Each is
/// restored through <see cref="DomainModels.TaskItem.Restore"/>, so the live copy is
/// the later version on every device, and loses
/// <see cref="LinkedTaskFlags.DeletedWithSource"/>, which has nothing left to say.
/// Nothing is fetched or written back: the next sync refreshes the tasks from the
/// source, and archives one whose item has gone by its usual rule.
/// </para>
/// </summary>
public sealed record RestoreDeletedLinkedTasksCommand(string ConnectorId, string Target)
{
    public const string NotConnectedCode = "linked_tasks.target_not_connected";

    public static readonly Error BlankPair = Error.Validation(
        "linked_tasks.restore_blank_pair",
        "Both the connector and the repository or product are needed.");

    /// <summary>The pair was disconnected between the card and the confirmation:
    /// its tasks would come back as a disconnected source's, which is not what was
    /// confirmed.</summary>
    public static Error NotConnected(string target) => Error.Conflict(
        NotConnectedCode,
        $"{target} is no longer connected, so its tasks were not restored.");
}

/// <summary>Runs the restore and answers how many tasks it brought back, for the
/// settings page's status line.</summary>
public sealed class RestoreDeletedLinkedTasksCommandHandler(ITaskRepository tasks, IConnectedTargets targets)
    : ICommandHandler<RestoreDeletedLinkedTasksCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RestoreDeletedLinkedTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.ConnectorId) || string.IsNullOrWhiteSpace(command.Target))
        {
            return RestoreDeletedLinkedTasksCommand.BlankPair;
        }

        if (targets.Get(command.ConnectorId, command.Target) is null)
        {
            return RestoreDeletedLinkedTasksCommand.NotConnected(command.Target);
        }

        var deleted = await tasks.ListDeletedAsync(cancellationToken).ConfigureAwait(false);
        var restored = 0;

        foreach (var task in deleted)
        {
            if (task.SourceRef is not { } source
                || !string.Equals(source.ConnectorId, command.ConnectorId, StringComparison.Ordinal)
                || !string.Equals(source.Target, command.Target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Cleared before the restore, so the one save carries both and the
            // restore's stamp is the last one the task took.
            task.SetSourceRef(source.WithFlag(LinkedTaskFlags.DeletedWithSource, set: false));
            task.Restore();
            await tasks.SaveAsync(task, cancellationToken).ConfigureAwait(false);
            restored++;
        }

        return restored;
    }
}
