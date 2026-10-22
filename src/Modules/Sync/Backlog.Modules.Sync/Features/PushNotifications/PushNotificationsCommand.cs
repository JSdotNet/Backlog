using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushNotifications;

/// <summary>
/// An owner instance hands over the notifications it published, or changed, since it last
/// managed to (local ADR 0038). The scope comes from the token and the notifications from the
/// body, and nothing in a <see cref="NotificationChange"/> can name an owner.
/// </summary>
public sealed record PushNotificationsCommand(OwnerScope Scope, IReadOnlyList<NotificationChange> Changes);

/// <summary>
/// Writes the batch and answers what it took. A notification held under another owner instance
/// is not written (one writer per notification, ADR 0038 §1) and comes back in
/// <see cref="PushNotificationsResponse.NotOwned"/> rather than refusing the batch, so one
/// colliding id never holds the owner's other notifications back.
/// </summary>
public sealed class PushNotificationsCommandHandler(INotificationReplica replica)
    : ICommandHandler<PushNotificationsCommand, Result<PushNotificationsResponse>>
{
    public async Task<Result<PushNotificationsResponse>> Handle(PushNotificationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_notifications", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var result = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        if (result.NotOwned.Count > 0)
        {
            activity?.SetTag(SyncTelemetry.ReasonTag, SyncErrorCodes.NotificationNotOwner);
        }

        return new PushNotificationsResponse(result.Accepted, result.NotOwned);
    }
}
