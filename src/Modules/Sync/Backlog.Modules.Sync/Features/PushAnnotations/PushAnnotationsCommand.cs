using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushAnnotations;

/// <summary>
/// A device hands over every annotation it has changed since it last managed
/// to. The scope comes from the token and the changes from the body, and the
/// two never mix — nothing in an <see cref="AnnotationChange"/> can name an
/// owner.
/// </summary>
public sealed record PushAnnotationsCommand(OwnerScope Scope, IReadOnlyList<AnnotationChange> Changes);

/// <summary>
/// Writes the batch and says how much of it was taken. No per-change outcome
/// and no merge: an annotation document is the unit of last-write-wins, the
/// replica keeps the later version and the desktop reconciles, so this handler
/// deliberately does not look inside a payload. "Later" is the replica's call
/// (<see cref="AnnotationChangePrecedence"/>), which is why
/// <see cref="PushAnnotationsResponse.Accepted"/> can be short of the batch: a
/// device re-sending a version the replica has already moved past — its echo
/// of what it pulled — is answered with a smaller count rather than an error,
/// because there is nothing for it to do about it.
/// </summary>
public sealed class PushAnnotationsCommandHandler(IAnnotationReplica replica)
    : ICommandHandler<PushAnnotationsCommand, Result<PushAnnotationsResponse>>
{
    public async Task<Result<PushAnnotationsResponse>> Handle(
        PushAnnotationsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_annotations", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        return new PushAnnotationsResponse(accepted);
    }
}
