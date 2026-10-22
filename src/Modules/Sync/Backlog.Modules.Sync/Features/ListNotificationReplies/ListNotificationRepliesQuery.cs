using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.ListNotificationReplies;

/// <summary>An owner instance asks for the replies addressed to it.</summary>
public sealed record ListNotificationRepliesQuery(OwnerScope Scope, Guid OwnerInstanceId, int MaxItems);

/// <summary>
/// The replies addressed to one owner instance, oldest first, for the device that speaks for
/// it: the one that last pushed its notifications. Any other device of the owner is answered
/// with none. No cursor, because the owner instance deletes each reply
/// once it has processed it — what is still listed is what it has not.
/// </summary>
public sealed class ListNotificationRepliesQueryHandler(INotificationReplica replica)
    : IQueryHandler<ListNotificationRepliesQuery, Result<PullNotificationRepliesResponse>>
{
    public async Task<Result<PullNotificationRepliesResponse>> Handle(ListNotificationRepliesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.list_notification_replies", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        var replies = await replica.ListReplies(query.Scope, query.OwnerInstanceId, query.MaxItems, cancellationToken);

        activity?.SetTag(SyncTelemetry.BatchSizeTag, replies.Count);

        return new PullNotificationRepliesResponse(replies);
    }
}
