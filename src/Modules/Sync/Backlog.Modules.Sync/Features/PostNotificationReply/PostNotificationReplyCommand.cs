using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PostNotificationReply;

/// <summary>A device of the owner — the phone, another desktop — replies to a question.</summary>
public sealed record PostNotificationReplyCommand(OwnerScope Scope, NotificationReplyPost Reply);

/// <summary>
/// Stores the reply against a notification the caller's own owner holds, or refuses it with
/// <see cref="SyncErrorCodes.NotificationNotFound"/>. This is the sync service's half of
/// <c>reply.foreign_owner</c> (local ADR 0038 §2): a device under another account finds no
/// notification in its own partition, so its reply is never stored where the owner instance
/// would read it. Whether the reply fits the question, and whether it is the first, is the
/// owner instance's to decide; the service reads none of it. Posting the same reply id twice
/// stores it once, so a phone's outbox may send it again.
/// <para>
/// A reply stored for the first time nudges the owner's desktops (ADR 0038 §4), so the owner
/// instance processes it within seconds rather than on its two-minute poll. A repeat send
/// stored nothing new and nudges nobody.
/// </para>
/// </summary>
public sealed class PostNotificationReplyCommandHandler(INotificationReplica replica, INotificationNudgeChannel nudges)
    : ICommandHandler<PostNotificationReplyCommand, Result<PostNotificationReplyResponse>>
{
    public async Task<Result<PostNotificationReplyResponse>> Handle(PostNotificationReplyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.post_notification_reply", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());

        var outcome = await replica.StoreReply(command.Scope, command.Reply, cancellationToken);

        if (outcome == NotificationReplyStoreOutcome.Stored)
        {
            await nudges.PublishAsync(command.Scope.OwnerId, NotificationNudgeKinds.Reply, cancellationToken);
        }

        return outcome switch
        {
            NotificationReplyStoreOutcome.Stored => new PostNotificationReplyResponse(command.Reply.Id, Stored: true),
            NotificationReplyStoreOutcome.AlreadyStored => new PostNotificationReplyResponse(command.Reply.Id, Stored: false),
            _ => Error.NotFound(
                SyncErrorCodes.NotificationNotFound,
                "There is no such notification for this owner instance here."),
        };
    }
}
