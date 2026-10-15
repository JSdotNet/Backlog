using Backlog.Modules.Tasks.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.RefileRepositoryTasks;

/// <summary>
/// What happens to the open tasks of a repository the person is archiving: moved
/// to <see cref="MoveToId"/>, or dropped (local ADR 0029; requirements
/// "Archiving a repository"). Keeping them where they are is the third choice and
/// is no command.
/// <para>
/// Open means Draft, Ready or In progress. Done and Archived work is history: it
/// stays under the repository it was done in, where it still counts toward pace.
/// </para>
/// <para>
/// <b>Move</b> replaces that one repository in the task's filing and nothing else.
/// It is not a rename: an issue link still points into the repository the issue
/// lives in, which is why <c>TaskItem.RenameRepository</c> is not used here.
/// <b>Drop</b> sets the Task Status <c>archived</c> and keeps the filing; nothing
/// is deleted. It sets the status outside the lifecycle graph, which allows
/// Archived only from Done, as a person's <c>!archived</c> edit does.
/// </para>
/// <para>
/// Idempotent, like the rename and the reconcile pass: after one run no open task
/// names the repository (move) or none is open (drop), so a second run is a pure
/// read. Soft-deleted entries are left alone, as the rename leaves them.
/// </para>
/// </summary>
/// <param name="RepositoryId">The <c>owner/name</c> of the repository being
/// archived, matched without regard to case.</param>
/// <param name="MoveToId">The <c>owner/name</c> the open tasks move to; null when
/// they are dropped.</param>
/// <param name="Drop">Whether the open tasks are dropped rather than moved.</param>
public sealed record RefileRepositoryTasksCommand(string RepositoryId, string? MoveToId, bool Drop)
{
    /// <summary>The request is not exactly one move to another repository or one
    /// drop.</summary>
    public static readonly Error Invalid = Error.Validation(
        "repository.refile_invalid",
        "Name the repository and either the repository to move its open tasks to, or drop them.");
}

/// <summary>Moves or drops the open tasks and answers how many it changed, for the
/// settings screen's status line, as the rename does.</summary>
public sealed class RefileRepositoryTasksCommandHandler(ITaskRepository entries, TimeProvider time)
    : ICommandHandler<RefileRepositoryTasksCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RefileRepositoryTasksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsValid(command)) return RefileRepositoryTasksCommand.Invalid;

        var repositoryId = command.RepositoryId.Trim();
        var moveToId = command.MoveToId?.Trim();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);

        // Snapshotted before the loop saves into the store it read from, as the
        // rename does.
        var stored = (await entries.ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var changed = 0;

        foreach (var entry in stored)
        {
            if (entry.DeletedAt is not null || !IsOpen(entry.Status)) continue;
            if (!entry.RepoIds.Any(id => Matches(id, repositoryId))) continue;

            if (command.Drop)
            {
                entry.SetStatus(EntryStatus.Archived, today);
            }
            else
            {
                entry.SetRepoIds(entry.RepoIds
                    .Select(id => Matches(id, repositoryId) ? moveToId! : id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());
            }

            await entries.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }

    /// <summary>Exactly one of a move to a different repository and a drop.</summary>
    private static bool IsValid(RefileRepositoryTasksCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.RepositoryId)) return false;

        var moves = !string.IsNullOrWhiteSpace(command.MoveToId);
        if (moves == command.Drop) return false;

        return !moves || !Matches(command.MoveToId!, command.RepositoryId.Trim());
    }

    private static bool IsOpen(EntryStatus status) =>
        status is EntryStatus.Draft or EntryStatus.Ready or EntryStatus.InProgress;

    /// <summary>Registry ids compare without regard to case, as everywhere else.</summary>
    private static bool Matches(string stored, string id) =>
        string.Equals(stored.Trim(), id, StringComparison.OrdinalIgnoreCase);
}
