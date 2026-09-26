using System.Security.Cryptography;

using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// Brings one of an item's files onto this machine: the step the intake runs
/// for every file it has just recorded, and the one Retry runs again.
/// <para>
/// <b>Never throws for a file.</b> Its caller is the sync client pulling a page,
/// and a throw there leaves the cursor where it was and replays the page for
/// ever (see <c>TaskReplicaMerge</c>). So every way a fetch can go wrong — the
/// service is unreachable, the blob has aged out of the store, the bytes do not
/// hash to what the capture said, the disk is full — ends as the file's
/// <see cref="InboxAttachment.LastError"/>, and the item stays in the inbox
/// with the rest of its content. Only the caller's own cancellation escapes.
/// </para>
/// <para>
/// <b>Written once.</b> A file whose recorded path — or the path it would be
/// written to — already holds bytes with the capture's digest is marked
/// downloaded without a fetch. That covers the replayed page, a Retry pressed
/// twice, and a crash between writing the file and saving the item.
/// </para>
/// </summary>
internal static class InboxAttachmentDownloads
{
    /// <summary>Fetches <paramref name="attachmentId"/> into the item's folder
    /// and records the outcome on the item. Answers the error recorded, or null
    /// when the file is on this machine. The caller saves the item.</summary>
    public static async Task<string?> DownloadAsync(
        InboxItem item,
        Guid attachmentId,
        IInboxAttachmentSource source,
        IInboxAttachmentFiles files,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var attachment = item.Attachments.First(candidate => candidate.Id == attachmentId);

        try
        {
            if (attachment.LocalPath is { } recorded
                && await files.HoldsAsync(recorded, attachment.Sha256, cancellationToken).ConfigureAwait(false))
            {
                if (!attachment.IsDownloaded) item.MarkAttachmentDownloaded(attachmentId, recorded, clock.GetUtcNow());
                return null;
            }

            var fileName = item.AttachmentFileName(attachmentId);
            var target = files.PathFor(item.Id, fileName);

            if (await files.HoldsAsync(target, attachment.Sha256, cancellationToken).ConfigureAwait(false))
            {
                item.MarkAttachmentDownloaded(attachmentId, target, clock.GetUtcNow());
                return null;
            }

            var fetched = await source.FetchAsync(attachmentId, cancellationToken).ConfigureAwait(false);
            if (fetched.IsFailure) return Fail(item, attachmentId, fetched.Error.Message, clock);

            var digest = Convert.ToHexStringLower(SHA256.HashData(fetched.Value));
            if (!string.Equals(digest, attachment.Sha256, StringComparison.Ordinal))
            {
                return Fail(
                    item,
                    attachmentId,
                    "The downloaded file does not match the one that was captured, so it was not kept.",
                    clock);
            }

            string written;
            try
            {
                written = await files.WriteAsync(item.Id, fileName, fetched.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Fetched fine; this machine could not keep it. Said as such, so
                // the person looks at their disk rather than at the sync service.
                return Fail(item, attachmentId, $"The file could not be saved on this machine: {failure.Message}", clock);
            }

            item.MarkAttachmentDownloaded(attachmentId, written, clock.GetUtcNow());

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            // Deliberately broad: see the class remarks. The message is the
            // adapter's, which for IO is the operating system's own sentence.
            return Fail(item, attachmentId, $"The file could not be downloaded: {failure.Message}", clock);
        }
    }

    private static string Fail(InboxItem item, Guid attachmentId, string error, TimeProvider clock)
    {
        item.MarkAttachmentFailed(attachmentId, error, clock.GetUtcNow());
        return item.Attachments.First(candidate => candidate.Id == attachmentId).LastError!;
    }
}
