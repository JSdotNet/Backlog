using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Answers the Inbox's <see cref="IInboxAttachmentSource"/> port over
/// <c>GET /api/sync/attachments/{id}</c> (local ADR 0014): one typed client on
/// the token pipeline, so a download is read under the owner in this device's
/// token and another owner's id reads as not found.
/// <para>
/// Transport, and one translation. The failure convention is
/// <see cref="SyncHttp"/>'s like every client here, but the words are not: this
/// answer lands on a file row in the Inbox, read by a person rather than branched
/// on by a screen, so the one failure with a cause the person can understand —
/// the blob is gone — is said in those terms.
/// </para>
/// </summary>
public sealed class InboxAttachmentSyncSource(HttpClient http) : IInboxAttachmentSource
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<Result<byte[]>> FetchAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var fetched = await SyncHttp.SendAsync<byte[]>(
            () => _http.GetAsync(SyncRoutes.AttachmentFor(attachmentId), HttpCompletionOption.ResponseContentRead, cancellationToken),
            async (content, token) => await content.ReadAsByteArrayAsync(token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (fetched.IsSuccess || fetched.Error.Type != ErrorType.NotFound) return fetched;

        // The store keeps a blob until the capture is acknowledged, or for thirty
        // days if nothing acknowledges it (ADR 0014 §Lifecycle). Either way it is
        // not coming back, and Retry will only repeat this.
        return Result.Failure<byte[]>(fetched.Error with
        {
            Message = "The file is no longer on the sync service. It is kept for 30 days, "
                + "or until another device has dealt with the capture.",
        });
    }
}
