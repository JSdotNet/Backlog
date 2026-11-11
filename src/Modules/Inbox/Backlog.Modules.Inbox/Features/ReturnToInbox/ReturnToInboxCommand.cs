using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ReturnToInbox;

/// <summary>
/// Takes a route back: the item is unprocessed again and no longer names where
/// it went. With <paramref name="DeleteTasks"/> — "Move to backlog" and "Create
/// plan", which made the entries — those entries are deleted first; without it —
/// "Link to task…", which made none — the task it named is left alone.
/// <paramref name="MadeDone"/> is Done now's undo: its one entry was born Done
/// and ticked, so Done does not count as started.
/// </summary>
public sealed record ReturnToInboxCommand(Guid Id, bool DeleteTasks, bool MadeDone = false);

/// <summary>
/// Settles the backlog first and the item second, so a refusal changes
/// nothing: the backlog refuses the whole deletion when any entry the route
/// made has been started — it is no longer a draft or ready — and the item stays
/// routed to all of them. An entry already gone is no reason to refuse.
/// </summary>
public sealed class ReturnToInboxCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    TimeProvider clock)
    : ICommandHandler<ReturnToInboxCommand, Result>
{
    public async Task<Result> Handle(ReturnToInboxCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        if (item.Routing is not { } routing)
        {
            return Result.Failure(InboxErrors.InvalidTransition(
                new InvalidInboxTransitionException(item.Status, "returned from the backlog").Message));
        }

        if (command.DeleteTasks && routing.TaskIds.Count > 0)
        {
            var deleted = await target.DeleteRoutedTasksAsync(routing.TaskIds, command.MadeDone, cancellationToken).ConfigureAwait(false);
            if (deleted.IsFailure) return deleted;
        }

        item.ReturnFromBacklog(clock.GetUtcNow());
        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
