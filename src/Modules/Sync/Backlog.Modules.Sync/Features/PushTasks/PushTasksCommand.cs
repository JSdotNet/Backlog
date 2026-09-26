using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
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
/// last-write-wins (.devbook/arc42/adr/0005): the replica keeps the later version and
/// the desktop reconciles, so this handler has nothing to decide and
/// deliberately does not look inside a payload. "Later" is the replica's call
/// (<c>TaskChangePrecedence</c>), which is why <see cref="PushTasksResponse.Accepted"/>
/// can be short of the batch: a device re-sending a version the replica has
/// already moved past — its echo of what it pulled — is answered with a smaller
/// count rather than an error, because there is nothing for it to do about it.
/// </para>
/// <para>
/// <b>The one thing it does look at is a tombstone for a capture.</b> The
/// desktop acknowledges a capture by pushing its tombstone here (local ADR
/// 0009), and that releases the capture's files from the attachment store (local
/// ADR 0014). The files are read from the capture as the replica held it before
/// the push, and released only when a tombstone is what the replica holds after
/// it — a tombstone the replica refused as stale leaves the capture, and its
/// files, where they were.
/// </para>
/// </summary>
public sealed class PushTasksCommandHandler(ITaskReplica replica, CaptureAttachmentRelease release)
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

        var withdrawn = await CapturesWithFiles(command, cancellationToken);

        var accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);

        foreach (var capture in withdrawn)
        {
            if (await replica.Find(command.Scope.OwnerId, capture.Id, cancellationToken) is { Change.DeletedAt: not null })
            {
                await release.Release(command.Scope.OwnerId, capture, cancellationToken);
            }
        }

        return new PushTasksResponse(accepted);
    }

    /// <summary>The live captures with files that this push tombstones, as the
    /// replica holds them now. One read per tombstone in the batch and none for
    /// anything else: a push is almost entirely edits, and a deletion of an
    /// ordinary task costs the lookup that tells it apart.</summary>
    private async Task<List<TaskChange>> CapturesWithFiles(PushTasksCommand command, CancellationToken cancellationToken)
    {
        var captures = new List<TaskChange>();

        foreach (var change in command.Changes)
        {
            if (change.DeletedAt is null) continue;

            if (await replica.Find(command.Scope.OwnerId, change.Id, cancellationToken) is
                {
                    Change: { DeletedAt: null, Task: { Type: CaptureInboxItemCommandHandler.CaptureType, Attachments.Count: > 0 } } held,
                })
            {
                captures.Add(held);
            }
        }

        return captures;
    }
}
