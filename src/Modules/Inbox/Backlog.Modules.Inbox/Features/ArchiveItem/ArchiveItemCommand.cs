using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ArchiveItem;

/// <summary>Dismisses an item. The one triage outcome that creates nothing in
/// another context; when the item came from the replica, the aggregate flags
/// it for the outbox so the phone stops offering it. With
/// <paramref name="DuplicateOf"/> it is dismissed as the same capture as that
/// item — "Archive as duplicate of…" — and remembers which: the item at the
/// root of that one's own duplicate chain, when it is itself a duplicate, and
/// refused as <c>inbox.duplicate.circular</c> when the chain leads back here.
/// <para>
/// With <paramref name="DuplicateOfTask"/> the id names a backlog task instead —
/// "Merge into a task", which archives the capture as a duplicate of the task it
/// was folded into. The Inbox cannot see Tasks, so there is no chain to walk and
/// no item to look up: the task is the caller's to have checked, as
/// <c>MergeIntoTaskCommandHandler</c> does before it writes the comment.
/// </para></summary>
public sealed record ArchiveItemCommand(Guid Id, Guid? DuplicateOf = null, bool DuplicateOfTask = false);

public sealed class ArchiveItemCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<ArchiveItemCommand, Result>
{
    public async Task<Result> Handle(ArchiveItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        var duplicateOf = command.DuplicateOf;

        if (duplicateOf is { } original && !command.DuplicateOfTask)
        {
            if (original == item.Id) return Result.Failure(InboxErrors.DuplicateOfItself);

            // The aggregate cannot see another item, so whether the one named
            // still exists is asked here, before anything changes.
            var target = await items.GetAsync(original, cancellationToken).ConfigureAwait(false);
            if (target is null) return Result.Failure(InboxErrors.DuplicateTargetNotFound);

            var root = await RootAsync(target, item.Id, cancellationToken).ConfigureAwait(false);
            if (root is null) return Result.Failure(InboxErrors.DuplicateCircular);

            duplicateOf = root.Id;
        }

        try
        {
            item.Archive(clock.GetUtcNow(), duplicateOf, task: duplicateOf is not null && command.DuplicateOfTask);
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }
        catch (ArgumentException)
        {
            return Result.Failure(InboxErrors.DuplicateOfItself);
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    /// <summary>
    /// The item at the root of <paramref name="target"/>'s duplicate chain — the
    /// one kept, which every duplicate of the thought should name, so a chain
    /// never grows past one step and archiving never leaves a thought with no
    /// item kept. Null when the chain leads back to <paramref name="itemId"/>:
    /// archiving it would archive every copy of the thought, the kept one
    /// included. A link to an item since deleted ends the chain at the last item
    /// still there; a chain that loops without passing the item — which only a
    /// hand-edited store could hold — ends where it would repeat.
    /// </summary>
    private async Task<InboxItem?> RootAsync(InboxItem target, Guid itemId, CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid> { target.Id };
        var root = target;

        // A link to a task ends the chain: the task is not an item, and the
        // item merged into it is the one kept on this side.
        while (root.DuplicateOf is { } next && !root.DuplicateOfTask)
        {
            if (next == itemId) return null;
            if (!seen.Add(next)) break;

            var parent = await items.GetAsync(next, cancellationToken).ConfigureAwait(false);
            if (parent is null) break;

            root = parent;
        }

        return root;
    }
}
