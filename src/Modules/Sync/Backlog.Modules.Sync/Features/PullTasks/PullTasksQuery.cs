using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PullTasks;

/// <summary>
/// A device asks for everything its owner has changed since the cursor it
/// holds. A null or empty <see cref="Since"/> means "from the beginning", which
/// is what a freshly paired device sends: there is no separate bootstrap, so the
/// first pull is the same call as the ten-thousandth.
/// </summary>
public sealed record PullTasksQuery(OwnerScope Scope, string? Since, int MaxItems);

/// <summary>
/// Verifies the cursor, reads one page, and mints the next cursor.
/// <para>
/// The order matters and is the point of the slice: the cursor is checked
/// against the caller's own owner <em>before</em> the replica is touched. A
/// store continuation carries its own feed range, so handing an unverified one
/// to the replica would read whichever partition it was minted for — the store
/// will not object, because the service reaches it under one identity that can
/// see every partition (.arc42/adr/0005 §Identity).
/// </para>
/// <para>
/// A fresh cursor comes back even when the page is empty. The feed's position
/// advances whether or not anything was there, and a client that kept its old
/// cursor would rescan from that point on every poll forever.
/// </para>
/// </summary>
public sealed class PullTasksQueryHandler(ITaskReplica replica, ISyncCursorCodec cursors)
    : IQueryHandler<PullTasksQuery, Result<PullTasksResponse>>
{
    public async Task<Result<PullTasksResponse>> Handle(
        PullTasksQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.pull_tasks", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        TaskReplicaCursor? cursor = null;

        if (!string.IsNullOrWhiteSpace(query.Since))
        {
            var verified = cursors.Verify(query.Since, query.Scope.OwnerId);
            if (verified.IsFailure)
            {
                SyncTelemetry.CursorRejected.Add(1, new KeyValuePair<string, object?>(
                    SyncTelemetry.ReasonTag, verified.Error.Code));

                activity?.SetStatus(ActivityStatusCode.Error, verified.Error.Code);

                return verified.Error;
            }

            cursor = new TaskReplicaCursor(verified.Value);
        }

        var page = await replica.ReadChanges(query.Scope.OwnerId, cursor, query.MaxItems, cancellationToken);

        activity?.SetTag(SyncTelemetry.BatchSizeTag, page.Changes.Count);

        return new PullTasksResponse(
            page.Changes,
            cursors.Mint(query.Scope.OwnerId, page.Cursor.Continuation),
            page.HasMore);
    }
}
