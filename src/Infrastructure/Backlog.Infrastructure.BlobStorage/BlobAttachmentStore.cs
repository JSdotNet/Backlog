using System.Buffers;
using System.Globalization;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Infrastructure.BlobStorage;

/// <summary>
/// The attachment store as one private blob container, <c>attachments</c>,
/// with each attachment at <c>{ownerId}/{attachmentId}</c> (local ADR 0014).
/// <para>
/// The owner prefix is what keeps one owner out of another's files: the service
/// reaches the container under one identity that can see every blob, and every
/// name this adapter builds starts from the owner the caller's token carried.
/// It is also what makes a whole owner's attachments one listable range.
/// </para>
/// <para>
/// <b>Uploads are staged as blocks and committed after.</b> A block blob's
/// staged blocks are invisible until a block list commits them, and Blob Storage
/// discards uncommitted blocks on its own after a week — which is exactly the
/// shape <see cref="IAttachmentStore.Stage"/> asks for. The handler checks the
/// size and digest between the two, so a refused upload never becomes a blob.
/// The digest and nothing else is kept as blob metadata; the content type is
/// the blob's own header, so a download serves it without a second read.
/// </para>
/// </summary>
internal sealed class BlobAttachmentStore(BlobContainerClient container) : IAttachmentStore
{
    /// <summary>The metadata key the digest is kept under. Blob metadata keys
    /// have to be C# identifiers, so no hyphen.</summary>
    internal const string Sha256MetadataKey = "sha256";

    /// <summary>How much of an upload one staged block carries. Large enough
    /// that a 25 MB file is a handful of round trips, small enough that one
    /// buffer is not a noticeable share of a consumption replica's memory.</summary>
    private const int BlockBytes = 4 * 1024 * 1024;

    /// <summary>The blob name for one owner's attachment. The owner first, so
    /// every name this adapter can build is inside the caller's own prefix.</summary>
    internal static string BlobName(OwnerId owner, Guid id) =>
        string.Create(CultureInfo.InvariantCulture, $"{owner}/{id:D}");

    public Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            try
            {
                var properties = await container.GetBlobClient(BlobName(owner, id)).GetPropertiesAsync(cancellationToken: cancellationToken);
                return Describe(id, properties.Value.ContentType, properties.Value.ContentLength, properties.Value.Metadata);
            }
            catch (RequestFailedException missing) when (missing.Status == 404)
            {
                return null;
            }
        });

    public Task<IStagedAttachment> Stage(OwnerId owner, Guid id, Stream content, CancellationToken cancellationToken = default) =>
        Guard<IStagedAttachment>(async () =>
        {
            ArgumentNullException.ThrowIfNull(content);

            var blob = container.GetBlockBlobClient(BlobName(owner, id));
            var blockIds = new List<string>();
            var buffer = ArrayPool<byte>.Shared.Rent(BlockBytes);

            try
            {
                int filled;
                while ((filled = await Fill(content, buffer, cancellationToken)) > 0)
                {
                    // Fixed-length ids, as a block list requires, unique per
                    // upload so two concurrent uploads under one name do not
                    // commit each other's blocks.
                    var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
                    using var block = new MemoryStream(buffer, 0, filled, writable: false);
                    await blob.StageBlockAsync(blockId, block, cancellationToken: cancellationToken);
                    blockIds.Add(blockId);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return new StagedBlob(blob, blockIds);
        });

    public Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            try
            {
                var download = await container.GetBlobClient(BlobName(owner, id)).DownloadStreamingAsync(cancellationToken: cancellationToken);
                var details = download.Value.Details;

                return new AttachmentContent(
                    Describe(id, details.ContentType, details.ContentLength, details.Metadata),
                    download.Value.Content);
            }
            catch (RequestFailedException missing) when (missing.Status == 404)
            {
                return (AttachmentContent?)null;
            }
        });

    public Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Guard(async () =>
        {
            await container.GetBlobClient(BlobName(owner, id)).DeleteIfExistsAsync(
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
            return true;
        });

    private static StoredAttachment Describe(Guid id, string contentType, long length, IDictionary<string, string> metadata) =>
        new(id, contentType, length, metadata.TryGetValue(Sha256MetadataKey, out var digest) ? digest : string.Empty);

    /// <summary>Reads until the buffer is full or the stream ends, so every
    /// block but the last is a whole block.</summary>
    private static async Task<int> Fill(Stream content, byte[] buffer, CancellationToken cancellationToken)
    {
        var filled = 0;
        int read;

        while (filled < BlockBytes
            && (read = await content.ReadAsync(buffer.AsMemory(filled, BlockBytes - filled), cancellationToken)) > 0)
        {
            filled += read;
        }

        return filled;
    }

    /// <summary>
    /// Turns "the store could not be reached" into the one failure the host
    /// renders as a 503, and lets everything else through.
    /// <para>
    /// Unreachable is a request that never got an answer (status 0), a
    /// transport failure, or the service saying it is unavailable or throttling.
    /// Anything else Storage answers is a bug or an outage nobody has
    /// classified, and belongs in the 500 the host already produces.
    /// Exceptions from the upload stream itself — the handler's cap — are not
    /// Storage's and pass through untouched.
    /// </para>
    /// </summary>
    private static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (RequestFailedException failure) when (failure.Status is 0 or 429 or 500 or 502 or 503 or 504)
        {
            throw Unavailable(failure);
        }
        catch (HttpRequestException failure)
        {
            throw Unavailable(failure);
        }
        catch (AggregateException failure) when (failure.InnerExceptions.All(inner => inner is RequestFailedException or HttpRequestException))
        {
            // The SDK's own retries give up with every attempt's failure in one
            // aggregate when the endpoint never answered at all.
            throw Unavailable(failure);
        }
    }

    private static SyncReplicaException Unavailable(Exception failure) => new(
        SyncErrorCodes.AttachmentStoreUnavailable,
        "The attachment store is not reachable yet. Try again shortly.",
        failure);

    private sealed class StagedBlob(BlockBlobClient blob, IReadOnlyList<string> blockIds) : IStagedAttachment
    {
        public Task Commit(StoredAttachment attachment, CancellationToken cancellationToken = default) =>
            Guard(async () =>
            {
                ArgumentNullException.ThrowIfNull(attachment);

                await blob.CommitBlockListAsync(
                    blockIds,
                    new CommitBlockListOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = attachment.ContentType },
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Sha256MetadataKey] = attachment.Sha256,
                        },
                    },
                    cancellationToken);
                return true;
            });
    }
}
