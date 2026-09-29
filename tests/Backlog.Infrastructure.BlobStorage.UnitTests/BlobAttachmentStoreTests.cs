using System.Security.Cryptography;

using Azure;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Infrastructure.BlobStorage.UnitTests;

/// <summary>
/// The store against an in-memory container: where it puts an attachment, what
/// it keeps beside the bytes, what a missing blob becomes, and which failures
/// are Storage being unreachable. The endpoint and handler tests run on the
/// module's in-memory store, so this is the only place the blob adapter's own
/// answers are pinned.
/// </summary>
public class BlobAttachmentStoreTests
{
    private const int BlockBytes = 4 * 1024 * 1024;

    private static readonly OwnerId Owner = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static readonly OwnerId Stranger = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static readonly Guid AttachmentId = Guid.Parse("0197a3c4-5d6e-7f80-9a1b-2c3d4e5f6a7b");

    /// <summary>
    /// The owner first, then the id — the prefix is what keeps one owner's
    /// files out of another's reach, since the service sees the whole container.
    /// </summary>
    [Fact]
    public async Task An_upload_lands_under_the_owner_then_the_attachment_id()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(1024);

        await Upload(store, Owner, content, Attachment(content));

        var name = Assert.Single(container.BlobNames);
        Assert.Equal("11111111-1111-1111-1111-111111111111/0197a3c4-5d6e-7f80-9a1b-2c3d4e5f6a7b", name);
        Assert.Equal(BlobAttachmentStore.BlobName(Owner, AttachmentId), name);
    }

    [Fact]
    public async Task Find_open_and_delete_address_the_name_the_upload_wrote()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(16);
        await Upload(store, Owner, content, Attachment(content));
        container.Addressed.Clear();

        await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken);
        (await store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken))!.Content.Dispose();
        await store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken);

        Assert.All(container.Addressed, name => Assert.Equal(BlobAttachmentStore.BlobName(Owner, AttachmentId), name));
        Assert.Equal(3, container.Addressed.Count);
    }

    /// <summary>Another owner asking for the same id reaches a name under its
    /// own prefix, where nothing is — indistinguishable from never uploaded.</summary>
    [Fact]
    public async Task Another_owner_asking_for_the_same_id_finds_nothing()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(16);
        await Upload(store, Owner, content, Attachment(content));

        Assert.Null(await store.Find(Stranger, AttachmentId, TestContext.Current.CancellationToken));
        Assert.Null(await store.Open(Stranger, AttachmentId, TestContext.Current.CancellationToken));

        await store.Delete(Stranger, AttachmentId, TestContext.Current.CancellationToken);

        Assert.NotNull(await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Commit_sends_the_declared_content_type_and_digest()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(2048);
        var attachment = Attachment(content, contentType: "image/png");

        await Upload(store, Owner, content, attachment);

        var commit = Assert.Single(container.Commits);
        Assert.Equal("image/png", commit.Options.HttpHeaders.ContentType);
        Assert.Equal(attachment.Sha256, commit.Options.Metadata[BlobAttachmentStore.Sha256MetadataKey]);
        Assert.Equal("sha256", BlobAttachmentStore.Sha256MetadataKey);
    }

    [Fact]
    public async Task Find_and_open_return_what_the_commit_declared()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(3000);
        var attachment = Attachment(content, contentType: "application/pdf");
        await Upload(store, Owner, content, attachment);

        var found = await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken);
        var opened = await store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken);

        Assert.Equal(attachment, found);
        Assert.NotNull(opened);
        await using var stream = opened.Content;
        Assert.Equal(attachment, opened.Attachment);
        Assert.Equal(content, await ReadAll(stream));
    }

    /// <summary>
    /// The store records the digest it is handed and does not compute one: the
    /// handler hashes the stream between Stage and Commit and never commits a
    /// mismatch. So whatever reaches Commit is what Find and Open give back.
    /// </summary>
    [Fact]
    public async Task The_store_keeps_exactly_the_digest_it_is_given()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(64);
        var declared = Attachment(content) with { Sha256 = "declared-by-the-caller" };

        await Upload(store, Owner, content, declared);

        Assert.Equal("declared-by-the-caller", (await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken))!.Sha256);

        var opened = await store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken);
        await using var stream = opened!.Content;
        Assert.Equal("declared-by-the-caller", opened.Attachment.Sha256);
    }

    /// <summary>Staged bytes are not the attachment: until Commit, nothing is
    /// there to find.</summary>
    [Fact]
    public async Task Staged_bytes_that_are_never_committed_are_never_found()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);

        using var content = new MemoryStream(Bytes(128));
        await store.Stage(Owner, AttachmentId, content, TestContext.Current.CancellationToken);

        Assert.Null(await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
    }

    /// <summary>Guideline ADR 0004: nothing stored is an expected answer, not
    /// an exception.</summary>
    [Fact]
    public async Task A_missing_blob_is_null_from_find_and_open()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer());

        Assert.Null(await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
        Assert.Null(await store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_a_missing_blob_is_not_an_error()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer());

        await store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Delete_removes_the_attachment()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes(16);
        await Upload(store, Owner, content, Attachment(content));

        await store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken);
        await store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken);

        Assert.Empty(container.BlobNames);
        Assert.Null(await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A stream that hands back a little at a time still fills whole blocks, so
    /// every block but the last is exactly the block size, and the commit lists
    /// exactly the blocks this upload staged, in order.
    /// </summary>
    [Fact]
    public async Task Content_is_staged_in_whole_blocks_and_committed_in_order()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var content = Bytes((2 * BlockBytes) + 1);

        using var trickle = new TrickleStream(content, maxRead: 64 * 1024);
        var staged = await store.Stage(Owner, AttachmentId, trickle, TestContext.Current.CancellationToken);
        await staged.Commit(Attachment(content), TestContext.Current.CancellationToken);

        Assert.Equal([BlockBytes, BlockBytes, 1], container.Staged.Select(block => block.Length));
        Assert.Equal(container.Staged.Select(block => block.BlockId), Assert.Single(container.Commits).BlockIds);

        var opened = await store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken);
        await using var stream = opened!.Content;
        Assert.Equal(content, await ReadAll(stream));
    }

    [Fact]
    public async Task An_empty_upload_commits_an_empty_block_list()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);

        await Upload(store, Owner, [], Attachment([]));

        Assert.Empty(container.Staged);
        Assert.Empty(Assert.Single(container.Commits).BlockIds);
        Assert.Equal(0, (await store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken))!.SizeBytes);
    }

    /// <summary>The handler bounds the upload stream; its refusal has to reach
    /// the handler as itself, not as the store being down.</summary>
    [Fact]
    public async Task An_exception_from_the_upload_stream_passes_through_unchanged()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer());
        var refusal = new InvalidDataException("The upload is larger than it declared.");

        using var content = new ThrowingStream(refusal);
        var thrown = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.Stage(Owner, AttachmentId, content, TestContext.Current.CancellationToken));

        Assert.Same(refusal, thrown);
    }

    /// <summary>A request body that breaks off mid-read is the stream's failure,
    /// not Storage's, however much it looks like a transport one.</summary>
    [Fact]
    public async Task An_io_failure_from_the_upload_stream_passes_through_unchanged()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer());
        var refusal = new IOException("The client went away.");

        using var content = new ThrowingStream(refusal);
        var thrown = await Assert.ThrowsAsync<IOException>(
            () => store.Stage(Owner, AttachmentId, content, TestContext.Current.CancellationToken));

        Assert.Same(refusal, thrown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Storage_that_cannot_answer_is_the_store_being_unavailable(int status)
    {
        var failure = new RequestFailedException(status, "Storage did not answer.");
        var store = new BlobAttachmentStore(new FakeBlobContainer { Failure = () => failure });

        var unavailable = await Assert.ThrowsAsync<SyncReplicaException>(
            () => store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));

        Assert.Equal(SyncErrorCodes.AttachmentStoreUnavailable, unavailable.Code);
        Assert.Same(failure, unavailable.InnerException);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Every_operation_maps_an_unavailable_store(string operation)
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        var staged = await StagedEmpty(store);
        container.Failure = () => new RequestFailedException(503, "Server busy.");

        var unavailable = await Assert.ThrowsAsync<SyncReplicaException>(
            () => Run(operation, store, staged, TestContext.Current.CancellationToken));

        Assert.Equal(SyncErrorCodes.AttachmentStoreUnavailable, unavailable.Code);
    }

    [Fact]
    public async Task A_transport_failure_is_the_store_being_unavailable()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer
        {
            Failure = () => new HttpRequestException("No such host is known."),
        });

        var unavailable = await Assert.ThrowsAsync<SyncReplicaException>(
            () => store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken));

        Assert.Equal(SyncErrorCodes.AttachmentStoreUnavailable, unavailable.Code);
    }

    /// <summary>What the SDK's retries throw when the endpoint never answered
    /// any attempt.</summary>
    [Fact]
    public async Task Retries_that_all_failed_to_reach_storage_are_the_store_being_unavailable()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer
        {
            Failure = () => new AggregateException(
                new RequestFailedException(0, "No answer."),
                new HttpRequestException("Connection refused.")),
        });

        var unavailable = await Assert.ThrowsAsync<SyncReplicaException>(
            () => store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken));

        Assert.Equal(SyncErrorCodes.AttachmentStoreUnavailable, unavailable.Code);
    }

    [Fact]
    public async Task An_aggregate_holding_anything_else_passes_through()
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer
        {
            Failure = () => new AggregateException(
                new HttpRequestException("Connection refused."),
                new InvalidOperationException("Something else.")),
        });

        await Assert.ThrowsAsync<AggregateException>(
            () => store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
    }

    /// <summary>Storage answered, and the answer is a bug or an unclassified
    /// outage: that is the host's 500, not a 503 that tells the device to retry.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(409)]
    public async Task A_storage_answer_that_is_not_unavailability_passes_through(int status)
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer
        {
            Failure = () => new RequestFailedException(status, "Storage refused."),
        });

        var refused = await Assert.ThrowsAsync<RequestFailedException>(
            () => store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));

        Assert.Equal(status, refused.Status);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_cancelled_call_is_cancelled_and_not_unavailable(string operation)
    {
        var store = new BlobAttachmentStore(new FakeBlobContainer());
        var staged = await StagedEmpty(store);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var thrown = await Record.ExceptionAsync(() => Run(operation, store, staged, cancelled.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    /// <summary>The caller's token is the one every Storage call and the
    /// stream read carry — none of them substitutes its own.</summary>
    [Fact]
    public async Task The_callers_token_reaches_storage_and_the_upload_stream()
    {
        var container = new FakeBlobContainer();
        var store = new BlobAttachmentStore(container);
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var content = Bytes(32);

        using var stream = new TrickleStream(content, maxRead: 8);
        var staged = await store.Stage(Owner, AttachmentId, stream, token);
        await staged.Commit(Attachment(content), token);
        await store.Find(Owner, AttachmentId, token);
        (await store.Open(Owner, AttachmentId, token))!.Content.Dispose();
        await store.Delete(Owner, AttachmentId, token);

        Assert.Equal(5, container.Tokens.Count);
        Assert.All(container.Tokens, seen => Assert.Equal(token, seen));
        Assert.NotEmpty(stream.Tokens);
        Assert.All(stream.Tokens, seen => Assert.Equal(token, seen));
    }

    /// <summary>
    /// None of the store's continuations comes back to the caller's context,
    /// as everywhere else in Infrastructure. Every Storage call and every stream
    /// read here finishes on another thread, so an await that captured the
    /// context would post its continuation to it.
    /// </summary>
    [Fact]
    public async Task No_continuation_returns_to_the_callers_context()
    {
        var container = new FakeBlobContainer { CompletesAsynchronously = true };
        var store = new BlobAttachmentStore(container);
        var context = new CountingContext();
        var content = Bytes(32);

        var staged = await On(context, () => store.Stage(Owner, AttachmentId, new TrickleStream(content, maxRead: 8, delayed: true), TestContext.Current.CancellationToken));
        await On(context, () => staged.Commit(Attachment(content), TestContext.Current.CancellationToken));
        await On(context, () => store.Find(Owner, AttachmentId, TestContext.Current.CancellationToken));
        (await On(context, () => store.Open(Owner, AttachmentId, TestContext.Current.CancellationToken)))!.Content.Dispose();
        await On(context, () => store.Delete(Owner, AttachmentId, TestContext.Current.CancellationToken));

        Assert.Equal(0, context.Posts);
    }

    public static TheoryData<string> Operations() => ["Find", "Stage", "Commit", "Open", "Delete"];

    private static Task Run(string operation, BlobAttachmentStore store, IStagedAttachment staged, CancellationToken cancellationToken) =>
        operation switch
        {
            "Find" => store.Find(Owner, AttachmentId, cancellationToken),
            "Stage" => store.Stage(Owner, AttachmentId, new MemoryStream(Bytes(16)), cancellationToken),
            "Commit" => staged.Commit(Attachment([]), cancellationToken),
            "Open" => store.Open(Owner, AttachmentId, cancellationToken),
            "Delete" => store.Delete(Owner, AttachmentId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static async Task<IStagedAttachment> StagedEmpty(BlobAttachmentStore store)
    {
        using var empty = new MemoryStream();
        return await store.Stage(Owner, AttachmentId, empty, TestContext.Current.CancellationToken);
    }

    private static async Task Upload(BlobAttachmentStore store, OwnerId owner, byte[] content, StoredAttachment attachment)
    {
        using var stream = new MemoryStream(content);
        var staged = await store.Stage(owner, AttachmentId, stream, TestContext.Current.CancellationToken);
        await staged.Commit(attachment, TestContext.Current.CancellationToken);
    }

    private static StoredAttachment Attachment(byte[] content, string contentType = "text/plain") =>
        new(AttachmentId, contentType, content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)));

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static async Task<byte[]> ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }

    /// <summary>Starts <paramref name="call"/> with <paramref name="context"/>
    /// current, so every await inside it that does not opt out captures it.</summary>
    private static TTask On<TTask>(SynchronizationContext context, Func<TTask> call)
        where TTask : Task
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            return call();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>A context that counts what is posted to it and runs it on the
    /// thread pool, current again, so a captured context stays captured.</summary>
    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;

        public int Posts => Volatile.Read(ref posts);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref posts);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SetSynchronizationContext(this);
                d(state);
            });
        }

        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>Hands its bytes back at most <c>maxRead</c> at a time, the way
    /// a request body arrives, and records the token each read carried.</summary>
    private sealed class TrickleStream(byte[] content, int maxRead, bool delayed = false) : Stream
    {
        private readonly MemoryStream inner = new(content, writable: false);

        public List<CancellationToken> Tokens { get; } = [];

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);

            if (delayed)
            {
                await Task.Delay(1, TestContext.Current.CancellationToken).ConfigureAwait(false);
            }

            return await inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxRead)], cancellationToken).ConfigureAwait(false);
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, maxRead));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingStream(Exception failure) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);

        public override int Read(byte[] buffer, int offset, int count) => throw failure;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
