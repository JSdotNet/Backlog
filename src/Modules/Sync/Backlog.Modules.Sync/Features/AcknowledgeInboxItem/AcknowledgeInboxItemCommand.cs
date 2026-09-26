using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.AcknowledgeInboxItem;

/// <summary>A device says the capture has been dealt with and the inbox need
/// not offer it again — the phone's own "Triage" button, or a desktop that has
/// no other way to say so.</summary>
public sealed record AcknowledgeInboxItemCommand(OwnerScope Scope, Guid Id);

/// <summary>
/// Tombstones the capture document.
/// <para>
/// This used to clear the source inbox id instead and leave the document alive,
/// because a capture was an ordinary task document and the desktop turned it
/// into work <em>under the same id</em> — so a tombstone here would have deleted
/// that work on every device. Neither half is true any more. A capture carries
/// its own kind token and never lands in a task table; the desktop's inbox
/// creates an item from it and routes that item to entries with ids of their
/// own, so the capture id names nothing but the capture. The desktop's own
/// acknowledgement is the same tombstone, pushed through the ordinary task
/// push, and this slice writing the same thing keeps one meaning for "dealt
/// with" whichever end says it.
/// </para>
/// <para>
/// A document belonging to another owner is not found rather than forbidden.
/// The lookup starts from the owner in the token, so there is no query here that
/// could see it, and saying "forbidden" would confirm the id exists. A document
/// that is not a capture is not found for the same reason: the route is about
/// captures, and an ordinary task's id is not the caller's to tombstone here.
/// </para>
/// <para>
/// Once the tombstone is stored, the capture's files are released from the
/// attachment store (local ADR 0014), from the capture as it was held — best
/// effort, so a store that is down never fails the acknowledgement.
/// </para>
/// </summary>
public sealed class AcknowledgeInboxItemCommandHandler(
    ITaskReplica replica,
    CaptureAttachmentRelease release,
    TimeProvider clock)
    : ICommandHandler<AcknowledgeInboxItemCommand, Result>
{
    public async Task<Result> Handle(
        AcknowledgeInboxItemCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await replica.Find(command.Scope.OwnerId, command.Id, cancellationToken);

        if (found is null
            || found.Change.DeletedAt is not null
            || !string.Equals(found.Change.Task.Type, CaptureInboxItemCommandHandler.CaptureType, StringComparison.Ordinal))
        {
            return Result.Failure(Error.NotFound(
                SyncErrorCodes.InboxItemNotFound,
                "No capture with that id is waiting for this owner."));
        }

        // Never earlier than the capture's own stamp. The replica refuses a
        // write that does not supersede the version it holds (TaskChangePrecedence),
        // and the capture was stamped by the phone's clock: a phone running
        // ahead of this service would otherwise have its acknowledgement
        // silently dropped and the capture stay in every inbox.
        var now = clock.GetUtcNow();
        if (now <= found.Change.UpdatedAt) now = found.Change.UpdatedAt.AddTicks(1);

        var acknowledged = found.Change with { UpdatedAt = now, DeletedAt = now };

        // Released only when the tombstone was taken. The replica refuses a
        // write it has already moved past, and releasing then would delete the
        // files of a capture that is still waiting.
        if (await replica.Upsert(command.Scope, [acknowledged], cancellationToken) > 0)
        {
            await release.Release(command.Scope.OwnerId, found.Change, cancellationToken);
        }

        return Result.Success();
    }
}
