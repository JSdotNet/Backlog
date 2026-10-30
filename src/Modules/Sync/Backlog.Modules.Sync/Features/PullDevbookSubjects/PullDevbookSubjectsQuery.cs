using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PullDevbookSubjects;

/// <summary>
/// A device asks for every devbook subject state its owner has changed since
/// the cursor it holds. A null or empty <see cref="Since"/> means "from the
/// beginning".
/// </summary>
public sealed record PullDevbookSubjectsQuery(OwnerScope Scope, string? Since, int MaxItems);

/// <summary>
/// Verifies the cursor, reads one page, and mints the next cursor — in that
/// order, for the reason <c>PullTasksQueryHandler</c> gives: the cursor is
/// checked against the caller's own owner before the replica is touched.
/// </summary>
public sealed class PullDevbookSubjectsQueryHandler(IDevbookSubjectReplica replica, ISyncCursorCodec cursors)
    : IQueryHandler<PullDevbookSubjectsQuery, Result<PullDevbookSubjectsResponse>>
{
    public async Task<Result<PullDevbookSubjectsResponse>> Handle(
        PullDevbookSubjectsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.pull_devbook_subjects", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        DevbookSubjectReplicaCursor? cursor = null;

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

            cursor = new DevbookSubjectReplicaCursor(verified.Value);
        }

        var page = await replica.ReadChanges(query.Scope.OwnerId, cursor, query.MaxItems, cancellationToken);

        activity?.SetTag(SyncTelemetry.BatchSizeTag, page.Changes.Count);

        return new PullDevbookSubjectsResponse(
            page.Changes,
            cursors.Mint(query.Scope.OwnerId, page.Cursor.Continuation),
            page.HasMore);
    }
}
