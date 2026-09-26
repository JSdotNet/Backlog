using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.Services;

/// <summary>
/// Deletes a capture's files once its acknowledgement tombstone is stored — the
/// one effect local ADR 0014 adds to local ADR 0009's tombstone.
/// <para>
/// <b>The list comes from the capture the service already held</b>, never from
/// the tombstone that replaced it. A tombstone is a device's payload, and one
/// that named files its capture never did would otherwise delete them; reading
/// the stored capture means a tombstone can only release what that capture
/// named.
/// </para>
/// <para>
/// <b>Best effort, and never the acknowledgement's problem.</b> The tombstone is
/// already written when this runs. A store that cannot be reached, or a delete
/// that fails, is logged and left to the 30-day lifecycle rule on the
/// container — failing the acknowledgement over it would leave the capture in
/// every inbox while its files were the only thing wrong.
/// </para>
/// </summary>
public sealed partial class CaptureAttachmentRelease(IAttachmentStore store, ILogger<CaptureAttachmentRelease> logger)
{
    /// <summary>Deletes every file <paramref name="capture"/> named. Never
    /// throws for a store failure; cancellation still propagates.</summary>
    public async Task Release(OwnerId owner, TaskChange capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);

        foreach (var attachment in capture.Task.Attachments ?? [])
        {
            try
            {
                await store.Delete(owner, attachment.Id, cancellationToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                LogReleaseFailed(logger, failure, attachment.Id, capture.Id);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not release attachment {AttachmentId} of acknowledged capture {CaptureId}; the storage lifecycle rule will remove it.")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception, Guid attachmentId, Guid captureId);
}
