using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PullAnnotations;

/// <summary>
/// A device asks for every annotation its owner has changed since the cursor it
/// holds. A null or empty <see cref="Since"/> means "from the beginning", which
/// is what a freshly paired device sends.
/// </summary>
public sealed record PullAnnotationsQuery(OwnerScope Scope, string? Since, int MaxItems);

/// <summary>
/// Verifies the cursor, reads one page, and mints the next cursor — in that
/// order, for the reason <c>PullTasksQueryHandler</c> gives: the cursor is
/// checked against the caller's own owner before the replica is touched.
/// </summary>
public sealed class PullAnnotationsQueryHandler(IAnnotationReplica replica, ISyncCursorCodec cursors)
    : IQueryHandler<PullAnnotationsQuery, Result<PullAnnotationsResponse>>
{
    public async Task<Result<PullAnnotationsResponse>> Handle(
        PullAnnotationsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var activity = SyncTelemetry.Source.StartActivity("sync.pull_annotations", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, query.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, query.Scope.DeviceId.ToString());

        if (string.IsNullOrWhiteSpace(query.Since)) return await PageAsync(null);

        var verified = cursors.Verify(query.Since, query.Scope.OwnerId);
        return verified switch
        {
            Ok<SyncCursor>(var since) => await PageAsync(new AnnotationReplicaCursor(since)),
            Error error => Rejected(error),
        };

        async Task<Result<PullAnnotationsResponse>> PageAsync(AnnotationReplicaCursor? cursor)
        {
            var page = await replica.ReadChanges(query.Scope.OwnerId, cursor, query.MaxItems, cancellationToken);

            activity?.SetTag(SyncTelemetry.BatchSizeTag, page.Changes.Count);

            return new PullAnnotationsResponse(
                page.Changes,
                cursors.Mint(query.Scope.OwnerId, page.Cursor.Continuation),
                page.HasMore);
        }

        Error Rejected(Error error)
        {
            SyncTelemetry.CursorRejected.Add(1, new KeyValuePair<string, object?>(
                SyncTelemetry.ReasonTag, error.Code));

            activity?.SetStatus(ActivityStatusCode.Error, error.Code);

            return error;
        }
    }
}
