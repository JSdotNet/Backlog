using System.Diagnostics.CodeAnalysis;

using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Backlog.Infrastructure.BlobStorage.UnitTests;

/// <summary>
/// A blob container held in memory, reached through the same virtual members
/// the SDK's own clients expose for mocking. It answers only the calls the store
/// makes, the way Storage answers them — a 404 for a blob that is not there,
/// staged blocks invisible until a block list commits them — and records what it
/// was asked, so a test can pin the names, headers and tokens the store sends.
/// </summary>
internal sealed class FakeBlobContainer : BlobContainerClient
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, CommittedBlob> blobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> stagedBlocks = new(StringComparer.Ordinal);

    /// <summary>When set, every call Storage would answer throws this instead.</summary>
    public Func<Exception>? Failure { get; set; }

    /// <summary>When set, every call finishes on a timer thread rather than
    /// inline, so an await on it resumes wherever the awaiter says to.</summary>
    public bool CompletesAsynchronously { get; set; }

    /// <summary>Every blob name a call addressed, in order.</summary>
    public List<string> Addressed { get; } = [];

    /// <summary>Every cancellation token a call carried, in order.</summary>
    public List<CancellationToken> Tokens { get; } = [];

    /// <summary>Every block staged, in the order it was staged.</summary>
    public List<(string BlockId, int Length)> Staged { get; } = [];

    /// <summary>Every block list committed, with the options it carried.</summary>
    public List<(string BlobName, IReadOnlyList<string> BlockIds, CommitBlockListOptions Options)> Commits { get; } = [];

    public IReadOnlyCollection<string> BlobNames
    {
        get
        {
            lock (gate)
            {
                return [.. blobs.Keys];
            }
        }
    }

    public override BlobClient GetBlobClient(string blobName) => new FakeBlobClient(this, blobName);

    protected override BlockBlobClient GetBlockBlobClientCore(string blobName) => new FakeBlockBlobClient(this, blobName);

    private async Task Answer(string blobName, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Addressed.Add(blobName);
            Tokens.Add(cancellationToken);
        }

        if (CompletesAsynchronously)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (Failure is { } failure)
        {
            throw failure();
        }
    }

    private CommittedBlob Committed(string blobName)
    {
        lock (gate)
        {
            return blobs.TryGetValue(blobName, out var blob)
                ? blob
                : throw new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
        }
    }

    private sealed record CommittedBlob(byte[] Content, string ContentType, IDictionary<string, string> Metadata);

    private sealed class FakeBlobClient(FakeBlobContainer container, string blobName) : BlobClient
    {
        public override string Name => blobName;

        public override async Task<Response<BlobProperties>> GetPropertiesAsync(
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            await container.Answer(blobName, cancellationToken).ConfigureAwait(false);
            var blob = container.Committed(blobName);

            return Response.FromValue(
                BlobsModelFactory.BlobProperties(
                    contentLength: blob.Content.LongLength,
                    contentType: blob.ContentType,
                    metadata: blob.Metadata),
                new StubResponse(200));
        }

        public override async Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await container.Answer(blobName, cancellationToken).ConfigureAwait(false);
            var blob = container.Committed(blobName);

            return Response.FromValue(
                BlobsModelFactory.BlobDownloadStreamingResult(
                    new MemoryStream(blob.Content, writable: false),
                    BlobsModelFactory.BlobDownloadDetails(
                        contentLength: blob.Content.LongLength,
                        contentType: blob.ContentType,
                        metadata: blob.Metadata)),
                new StubResponse(200));
        }

        public override async Task<Response<bool>> DeleteIfExistsAsync(
            DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.None,
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            await container.Answer(blobName, cancellationToken).ConfigureAwait(false);

            lock (container.gate)
            {
                return Response.FromValue(container.blobs.Remove(blobName), new StubResponse(202));
            }
        }
    }

    private sealed class FakeBlockBlobClient(FakeBlobContainer container, string blobName) : BlockBlobClient
    {
        public override string Name => blobName;

        public override async Task<Response<BlockInfo>> StageBlockAsync(
            string base64BlockId,
            Stream content,
            BlockBlobStageBlockOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await container.Answer(blobName, cancellationToken).ConfigureAwait(false);

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);

            lock (container.gate)
            {
                container.stagedBlocks[base64BlockId] = copy.ToArray();
                container.Staged.Add((base64BlockId, (int)copy.Length));
            }

            // The store never reads the answer, and the model factory cannot
            // build one; the status is what a real stage returns.
            return Response.FromValue<BlockInfo>(null!, new StubResponse(201));
        }

        public override async Task<Response<BlobContentInfo>> CommitBlockListAsync(
            IEnumerable<string> base64BlockIds,
            CommitBlockListOptions options,
            CancellationToken cancellationToken = default)
        {
            await container.Answer(blobName, cancellationToken).ConfigureAwait(false);

            var blockIds = base64BlockIds.ToList();

            lock (container.gate)
            {
                container.Commits.Add((blobName, blockIds, options));
                container.blobs[blobName] = new CommittedBlob(
                    [.. blockIds.SelectMany(blockId => container.stagedBlocks[blockId])],
                    options.HttpHeaders?.ContentType ?? string.Empty,
                    new Dictionary<string, string>(options.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal));
            }

            return Response.FromValue<BlobContentInfo>(null!, new StubResponse(201));
        }
    }

    /// <summary>The raw response a <see cref="Response{T}"/> carries. The store
    /// reads the value only, so nothing here is ever looked at.</summary>
    private sealed class StubResponse(int status) : Response
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = null;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }
    }
}
