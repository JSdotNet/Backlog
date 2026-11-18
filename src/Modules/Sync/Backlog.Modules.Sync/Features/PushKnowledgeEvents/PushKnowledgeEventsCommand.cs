using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushKnowledgeEvents;

/// <summary>
/// A desktop hands over every knowledge event and series it has changed since it last
/// managed to. The scope comes from the token and the changes from the body, and nothing in
/// a <see cref="KnowledgeEventChange"/> can name an owner.
/// </summary>
public sealed record PushKnowledgeEventsCommand(OwnerScope Scope, IReadOnlyList<KnowledgeEventChange> Changes);

/// <summary>
/// Writes the batch and says how much of it was taken, on
/// <c>PushDevbookSubjectsCommandHandler</c>'s terms: no per-change outcome, no look inside a
/// document, and a count short of the batch by every change the replica already held a later
/// version of (<see cref="KnowledgeEventChangePrecedence"/>).
/// </summary>
public sealed class PushKnowledgeEventsCommandHandler(IKnowledgeEventReplica replica)
    : ICommandHandler<PushKnowledgeEventsCommand, Result<PushKnowledgeEventsResponse>>
{
    public async Task<Result<PushKnowledgeEventsResponse>> Handle(
        PushKnowledgeEventsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_knowledge_events", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        return new PushKnowledgeEventsResponse(accepted);
    }
}
