using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Features.ListInbox;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.EditCapture;

/// <summary>The phone rewords one of its captures while the desktop has not
/// decided it yet: a new <paramref name="Title"/> and <paramref name="BodyMd"/>,
/// a null body meaning none. The endpoint has bounded both before this is
/// built.</summary>
public sealed record EditCaptureCommand(OwnerScope Scope, Guid Id, string Title, string? BodyMd);

/// <summary>
/// Replaces the title and body of a live capture document and nothing else.
/// <para>
/// The id, the capture's time, its source, its tags and person, and the files it
/// names are what the capture said when it was made, and they stay as they are:
/// an edit carries no attachment change, because a capture's files never change
/// after it was posted.
/// </para>
/// <para>
/// <b>The service stamps the new version.</b> The replica orders versions by
/// their stamp (<see cref="TaskChangePrecedence"/>), and a phone's clock has no
/// say in that, so the edit is stamped with this service's time — or one tick
/// after the held version when that time is not later, since an edit stamped at
/// or before the held version would be refused as stale.
/// </para>
/// <para>
/// <b>Only a waiting capture can be edited.</b> A capture the desktop already
/// decided is tombstoned, and the answer is the one acknowledging it gives —
/// not found — so the phone hears "already dealt with" either way. The same
/// answer covers an id that is not a capture or belongs to another owner, for
/// the reason <c>AcknowledgeInboxItemCommandHandler</c> gives. A desktop
/// tombstone that lands between the read and the write wins (a capture's
/// tombstone is final on the replica), so a refused write is answered the same.
/// </para>
/// </summary>
public sealed class EditCaptureCommandHandler(ITaskReplica replica, TimeProvider clock)
    : ICommandHandler<EditCaptureCommand, Result<InboxItem>>
{
    public async Task<Result<InboxItem>> Handle(
        EditCaptureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var found = await replica.Find(command.Scope.OwnerId, command.Id, cancellationToken);

        if (found is null
            || found.Change.DeletedAt is not null
            || !string.Equals(found.Change.Task.Type, CaptureInboxItemCommandHandler.CaptureType, StringComparison.Ordinal))
        {
            return NotWaiting();
        }

        var now = clock.GetUtcNow();
        if (now <= found.Change.UpdatedAt) now = found.Change.UpdatedAt.AddTicks(1);

        var edited = found.Change with
        {
            UpdatedAt = now,
            Task = found.Change.Task with { Title = command.Title, ContentMd = command.BodyMd ?? string.Empty },
        };

        if (await replica.Upsert(command.Scope, [edited], cancellationToken) == 0)
        {
            return NotWaiting();
        }

        return ListInboxQueryHandler.Project(edited);
    }

    private static Result<InboxItem> NotWaiting() => Result.Failure<InboxItem>(Error.NotFound(
        SyncErrorCodes.InboxItemNotFound,
        "No capture with that id is waiting for this owner."));
}
