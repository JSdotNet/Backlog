using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.DeleteNotificationReply;

/// <summary>An owner instance removes a reply it has processed (local ADR 0038 §2).</summary>
public sealed record DeleteNotificationReplyCommand(OwnerScope Scope, Guid ReplyId);

/// <summary>
/// Deletes the reply when the calling device speaks for the owner instance it is addressed
/// to, and answers
/// <see cref="SyncErrorCodes.NotificationReplyNotFound"/> otherwise — for a reply that is gone
/// already as much as for one addressed elsewhere, so a guessed id confirms nothing.
/// </summary>
public sealed class DeleteNotificationReplyCommandHandler(INotificationReplica replica)
    : ICommandHandler<DeleteNotificationReplyCommand, Result>
{
    public async Task<Result> Handle(DeleteNotificationReplyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.delete_notification_reply", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());

        return await replica.DeleteReply(command.Scope, command.ReplyId, cancellationToken)
            ? Result.Success()
            : Result.Failure(Error.NotFound(SyncErrorCodes.NotificationReplyNotFound, "There is no such reply for this device."));
    }
}
