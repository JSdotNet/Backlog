using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushDevbookSubjects;

/// <summary>
/// A device hands over every devbook subject state it has changed since it last
/// managed to. The scope comes from the token and the changes from the body, and
/// nothing in a <see cref="DevbookSubjectChange"/> can name an owner.
/// </summary>
public sealed record PushDevbookSubjectsCommand(OwnerScope Scope, IReadOnlyList<DevbookSubjectChange> Changes);

/// <summary>
/// Writes the batch and says how much of it was taken, on
/// <c>PushAnnotationsCommandHandler</c>'s terms: no per-change outcome, no look
/// inside a payload, and a count short of the batch by every change the replica
/// already held a later version of (<see cref="DevbookSubjectChangePrecedence"/>).
/// </summary>
public sealed class PushDevbookSubjectsCommandHandler(IDevbookSubjectReplica replica)
    : ICommandHandler<PushDevbookSubjectsCommand, Result<PushDevbookSubjectsResponse>>
{
    public async Task<Result<PushDevbookSubjectsResponse>> Handle(
        PushDevbookSubjectsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_devbook_subjects", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        return new PushDevbookSubjectsResponse(accepted);
    }
}
