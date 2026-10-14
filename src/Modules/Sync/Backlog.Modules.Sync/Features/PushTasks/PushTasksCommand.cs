using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;

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
/// <para>
/// A note's tombstone releases its files the same way. A note is the Inbox item
/// that syncs both ways (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>),
/// and its tombstone is what the desktop pushes when it archives or deletes one.
/// </para>
/// </summary>
public sealed partial class PushTasksCommandHandler(
    ITaskReplica replica,
    CaptureAttachmentRelease release,
    ILogger<PushTasksCommandHandler> logger)
    : ICommandHandler<PushTasksCommand, Result<PushTasksResponse>>
{
    /// <summary>The kind token a note document carries. A literal, for the reason
    /// <see cref="CaptureInboxItemCommandHandler.CaptureType"/> is one: the service
    /// may not reference the Inbox that names it.</summary>
    internal const string NoteType = "note";

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

        int accepted;
        try
        {
            accepted = await replica.Upsert(command.Scope, command.Changes, cancellationToken);
        }
        catch (Exception) when (withdrawn.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            // The replica writes a batch several documents at once, so a push
            // that failed on one document may already have written a tombstone
            // beside it. Release now or never: the retry reads that capture as
            // a tombstone already and would not count it as withdrawn. The read
            // back below confirms which tombstones landed, and the push's own
            // failure is what the device hears whatever the release does.
            await ReleaseAfterFailedPush(command.Scope.OwnerId, withdrawn, cancellationToken);
            throw;
        }

        await ReleaseLanded(command.Scope.OwnerId, withdrawn, cancellationToken);

        return new PushTasksResponse(accepted);
    }

    /// <summary>Releases the files of every withdrawn capture whose tombstone the
    /// replica now holds.</summary>
    private async Task ReleaseLanded(OwnerId owner, List<TaskChange> withdrawn, CancellationToken cancellationToken)
    {
        foreach (var capture in withdrawn)
        {
            if (await replica.Find(owner, capture.Id, cancellationToken) is { Change.DeletedAt: not null })
            {
                await release.Release(owner, capture, cancellationToken);
            }
        }
    }

    /// <summary>
    /// <see cref="ReleaseLanded"/> on the way out of a failed push. Best effort,
    /// as every release is: a replica that cannot be read back, or a push
    /// cancelled meanwhile, is logged and leaves the files to the 30-day
    /// lifecycle rule rather than replacing the failure the device must hear.
    /// </summary>
    private async Task ReleaseAfterFailedPush(OwnerId owner, List<TaskChange> withdrawn, CancellationToken cancellationToken)
    {
        try
        {
            await ReleaseLanded(owner, withdrawn, cancellationToken);
        }
        catch (Exception failure)
        {
            LogReleaseAfterFailedPushFailed(logger, failure, withdrawn.Count);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not release the files of {Count} withdrawn captures after a push failed; the storage lifecycle rule will remove any that were tombstoned.")]
    private static partial void LogReleaseAfterFailedPushFailed(ILogger logger, Exception exception, int count);

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
                    Change: { DeletedAt: null, Task: { Type: CaptureInboxItemCommandHandler.CaptureType or NoteType, Attachments.Count: > 0 } } held,
                })
            {
                captures.Add(held);
            }
        }

        return captures;
    }
}
