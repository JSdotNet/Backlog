using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PullNotifications;

/// <summary>A viewer asks for every notification its owner's instances published or changed
/// since the cursor it holds; null or empty means from the beginning.</summary>
public sealed record PullNotificationsQuery(OwnerScope Scope, string? Since, int MaxItems);

/// <summary>
/// Verifies the cursor, reads one page, and mints the next cursor — in the order
/// <c>PullAnnotationsQueryHandler</c> does, so the cursor is checked against the caller's own
/// owner before the replica is touched.
/// </summary>
public sealed class PullNotificationsQueryHandler(INotificationReplica replica, ISyncCursorCodec cursors)
    : IQueryHandler<PullNotificationsQuery, Result<PullNotificationsResponse>>
{
    public async Task<Result<PullNotificationsResponse>> Handle(PullNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.pull_notifications", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        NotificationReplicaCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Since))
        {
            var verified = cursors.Verify(query.Since, query.Scope.OwnerId);
            if (verified.IsFailure)
            {
                SyncTelemetry.CursorRejected.Add(1, new KeyValuePair<string, object?>(SyncTelemetry.ReasonTag, verified.Error.Code));
                activity?.SetStatus(ActivityStatusCode.Error, verified.Error.Code);

                return verified.Error;
            }

            cursor = new NotificationReplicaCursor(verified.Value);
        }

        var page = await replica.ReadChanges(query.Scope.OwnerId, cursor, query.MaxItems, cancellationToken);

        activity?.SetTag(SyncTelemetry.BatchSizeTag, page.Changes.Count);

        return new PullNotificationsResponse(page.Changes, cursors.Mint(query.Scope.OwnerId, page.Cursor.Continuation), page.HasMore);
    }
}
