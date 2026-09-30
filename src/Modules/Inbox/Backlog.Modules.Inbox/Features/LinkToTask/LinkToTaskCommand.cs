using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.LinkToTask;

/// <summary>Records that an item is already a task the backlog has — "Link to
/// task…" — so it leaves the queue routed to that task and no new one is made.</summary>
public sealed record LinkToTaskCommand(Guid Id, Guid TaskId);

/// <summary>
/// The Inbox side only. The task is not rewritten: its source is fixed when it
/// is made, and the decision being recorded is the Inbox's — this item needs no
/// entry of its own because one exists.
/// <para>
/// With the task port, the task is looked up first: one that is gone or
/// archived is refused and the item is left as it was, and the routing records
/// the task's own repositories, which is where the work lives. A host without
/// the port takes the id as given and records the item's repositories — the
/// same trust the single route gives its target's answer.
/// </para>
/// </summary>
public sealed class LinkToTaskCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxTaskReferences? taskReferences = null)
    : ICommandHandler<LinkToTaskCommand, Result>
{
    public async Task<Result> Handle(LinkToTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        IReadOnlyList<string> repoIds = [.. item.RepoIds];

        if (taskReferences is not null)
        {
            var tasks = await taskReferences.AllTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasks.FirstOrDefault(task => task.Id == command.TaskId) is not { } task) return Result.Failure(InboxErrors.TaskNotFound);

            repoIds = [.. task.RepoIds];
        }

        try
        {
            item.LinkToTask(command.TaskId, repoIds, clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }
        catch (ArgumentException)
        {
            return Result.Failure(InboxErrors.TaskNotFound);
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
