using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.MergeIntoTask;

/// <summary>"Merge into a task": folds a capture into the backlog task
/// <paramref name="TaskId"/> it repeats — the capture's title, link and notes
/// become a comment on the task, and the capture is archived as a duplicate of
/// it.</summary>
public sealed record MergeIntoTaskCommand(Guid Id, Guid TaskId);

/// <summary>
/// Two writes in two contexts, in the order that leaves the least to explain
/// when the second fails.
/// <para>
/// <b>Everything the Inbox can refuse is refused first.</b> The item must exist
/// and still be open — a merge is a triage decision, and an item that has had
/// one is not merged — and, with the task port, the task must be one the backlog
/// still has. Only then is anything written, so a refusal leaves both sides as
/// they were.
/// </para>
/// <para>
/// <b>The comment before the archive.</b> The comment goes through
/// <see cref="IInboxBacklogTarget.CommentOnTaskAsync"/>, which may still refuse
/// it — a capture whose notes carry a heading, a checklist item or a fence is
/// not prose Tasks' comment rule will take — and a refused comment leaves the
/// item open. Archived first, a refusal would leave a capture archived as a
/// duplicate of a task that never heard of it.
/// </para>
/// <para>
/// <b>The archive is <see cref="ArchiveItemCommand"/>'s</b>, with
/// <see cref="ArchiveItemCommand.DuplicateOfTask"/>, so archiving keeps one set
/// of rules whatever it is archived as. Should it fail once the comment is
/// written, the comment stays — it may already have been read — and the failure
/// says so (<c>inbox.merge.archive_failed</c>), because merging again would
/// write it twice.
/// </para>
/// </summary>
public sealed class MergeIntoTaskCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    ICommandHandler<ArchiveItemCommand, Result> archive,
    IInboxTaskReferences? taskReferences = null)
    : ICommandHandler<MergeIntoTaskCommand, Result>
{
    /// <summary>The code the comment answers with when the task is gone — the
    /// one Tasks' comment rule publishes, read here as a fact about the task
    /// rather than learnt from Tasks.</summary>
    private const string CommentTaskNotFound = "item.not_found";

    public async Task<Result> Handle(MergeIntoTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        if (!item.IsOpen)
        {
            return Result.Failure(InboxErrors.InvalidTransition(
                "It has had its decision already, so it cannot be merged into a task."));
        }

        if (taskReferences is not null)
        {
            var tasks = await taskReferences.AllTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasks.All(task => task.Id != command.TaskId)) return Result.Failure(InboxErrors.MergeTaskNotFound);
        }

        var commented = await target
            .CommentOnTaskAsync(
                new InboxMergeRequestDto(item.Id, command.TaskId, item.Title, item.SourceUrl, item.BodyMd),
                cancellationToken)
            .ConfigureAwait(false);

        if (commented.IsFailure)
        {
            return Result.Failure(commented.Error.Code == CommentTaskNotFound ? InboxErrors.MergeTaskNotFound : commented.Error);
        }

        var archived = await archive
            .Handle(new ArchiveItemCommand(item.Id, command.TaskId, DuplicateOfTask: true), cancellationToken)
            .ConfigureAwait(false);

        return archived.IsSuccess ? archived : Result.Failure(InboxErrors.MergeArchiveFailed(archived.Error.Message));
    }
}
