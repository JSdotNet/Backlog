using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.PushTasks;

/// <summary>
/// A device hands over everything it has changed since it last managed to.
/// <para>
/// The scope comes from the token and the changes come from the body, and the
/// two never mix: nothing in a <see cref="TaskChange"/> can name an owner, so
/// there is no batch a client could compose that writes outside its own
/// partition.
/// </para>
/// </summary>
public sealed record PushTasksCommand(OwnerScope Scope, IReadOnlyList<TaskChange> Changes);

/// <summary>
/// Writes the batch and says how much of it was taken.
/// <para>
/// There is no per-change outcome and no merge. A task document is the unit of
/// last-write-wins (.arc42/adr/0005): the replica keeps whichever version
/// arrived last and the desktop reconciles, so this handler has nothing to
/// decide and deliberately does not look inside a payload.
/// </para>
/// </summary>
public sealed class PushTasksCommandHandler(ITaskReplica replica)
    : ICommandHandler<PushTasksCommand, Result<PushTasksResponse>>
{
    public async Task<Result<PushTasksResponse>> Handle(
        PushTasksCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = SyncTelemetry.Source.StartActivity("sync.push_tasks", ActivityKind.Internal);
        activity?.SetTag(SyncTelemetry.OwnerIdTag, command.Scope.OwnerId.ToString());
        activity?.SetTag(SyncTelemetry.DeviceIdTag, command.Scope.DeviceId.ToString());
        activity?.SetTag(SyncTelemetry.BatchSizeTag, command.Changes.Count);

        var accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        return new PushTasksResponse(accepted);
    }
}
