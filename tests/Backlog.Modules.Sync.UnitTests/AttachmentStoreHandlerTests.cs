using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.AcknowledgeInboxItem;
using Backlog.Modules.Sync.Features.CaptureInboxItem;
using Backlog.Modules.Sync.Features.PushTasks;
using Backlog.Modules.Sync.Features.StoreAttachment;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The attachment slices against the in-memory store: the upload's cap and
/// digest, a capture's refusal of files it was never given, and the release on
/// acknowledgement that must never fail the acknowledgement (local ADR 0014).
/// </summary>
public sealed class AttachmentStoreHandlerTests
{
    private static readonly OwnerScope Scope = new(OwnerId.New(), DeviceId.New());

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAttachmentStore _store = new();
    private readonly InMemoryTaskReplica _replica = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upload_is_stored_with_its_size_and_digest()
    {
        var bytes = Bytes(100);

        var result = await Upload(Guid.CreateVersion7(), bytes, maxBytes: 1_000);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal(100, result.Value.Attachment.SizeBytes);
        Assert.Equal(Sha256(bytes), result.Value.Attachment.Sha256);
    }

    /// <summary>A body with no length that runs past the cap is stopped there,
    /// and what was read is never committed.</summary>
    [Fact]
    public async Task An_upload_over_the_cap_is_refused_and_nothing_is_stored()
    {
        var id = Guid.CreateVersion7();

        var result = await Upload(id, Bytes(2_000), maxBytes: 1_000);

        Assert.Equal(SyncErrorCodes.AttachmentTooLarge, result.Error.Code);
        Assert.Contains("Content-Length", result.Error.Message, StringComparison.Ordinal);
        Assert.Null(await _store.Find(Scope.OwnerId, id, Cancellation));
    }

    [Fact]
    public async Task An_upload_at_exactly_the_cap_is_stored()
    {
        Assert.True((await Upload(Guid.CreateVersion7(), Bytes(1_000), maxBytes: 1_000)).IsSuccess);
    }

    [Fact]
    public async Task Bytes_that_miss_their_declared_digest_are_refused_and_not_committed()
    {
        var id = Guid.CreateVersion7();

        var result = await Upload(id, Bytes(10), maxBytes: 1_000, sha256: Sha256(Bytes(10, seed: 2)));

        Assert.Equal(SyncErrorCodes.AttachmentInvalid, result.Error.Code);
        Assert.Contains(SyncRoutes.AttachmentSha256Header, result.Error.Message, StringComparison.Ordinal);
        Assert.Null(await _store.Find(Scope.OwnerId, id, Cancellation));
    }

    /// <summary>A repeat is recognised by its declared digest alone, so the
    /// body is never read — the stream here would throw if it were.</summary>
    [Fact]
    public async Task A_repeat_upload_is_answered_without_reading_the_body()
    {
        var id = Guid.CreateVersion7();
        var bytes = Bytes(10);
        await Upload(id, bytes, maxBytes: 1_000);

        var repeat = await new StoreAttachmentCommandHandler(_store).Handle(
            new StoreAttachmentCommand(Scope, id, "image/png", Sha256(bytes), new UnreadableStream(), 1_000),
            Cancellation);

        Assert.True(repeat.IsSuccess);
        Assert.False(repeat.Value.Created);
    }

    [Fact]
    public async Task A_capture_naming_an_attachment_not_uploaded_is_refused_with_its_id()
    {
        var missing = new AttachmentMetadata(Guid.CreateVersion7(), "a.png", "image/png", 10, Sha256(Bytes(10)));

        var result = await Capture(new CaptureInboxItemCommand(Scope, "Keynote", "phone", Attachments: [missing]));

        Assert.Equal(SyncErrorCodes.CaptureAttachmentMissing, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Contains(missing.Id.ToString("D"), result.Error.Message, StringComparison.Ordinal);
        Assert.Null(await _replica.Find(Scope.OwnerId, missing.Id, Cancellation));
    }

    [Fact]
    public async Task A_two_field_capture_needs_no_store_and_carries_no_attachments()
    {
        var result = await Capture(new CaptureInboxItemCommand(Scope, "Call the dentist", "phone"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Item.Attachments);
        Assert.Null((await _replica.Find(Scope.OwnerId, result.Value.Item.Id, Cancellation))!.Change.Task.Attachments);
    }

    [Fact]
    public async Task Acknowledging_a_capture_releases_its_attachments()
    {
        var capture = await CaptureWithAttachment();

        var result = await Acknowledge(capture.Id, new CaptureAttachmentRelease(_store, new RecordingLogger()));

        Assert.True(result.IsSuccess);
        Assert.Null(await _store.Find(Scope.OwnerId, capture.Attachments![0].Id, Cancellation));
    }

    /// <summary>A note's tombstone, which the desktop pushes when it archives or
    /// deletes the note, releases the files the note named, as a capture's does
    /// (.devbook/arc42/06-runtime-view.md#mobile-note-sync). A live edit of the note
    /// releases nothing.</summary>
    [Fact]
    public async Task A_notes_tombstone_releases_its_attachments_and_an_edit_does_not()
    {
        var bytes = Bytes(32);
        var fileId = Guid.CreateVersion7();
        await Upload(fileId, bytes, maxBytes: 1_000);
        var file = new AttachmentMetadata(fileId, "slide.png", "image/png", bytes.Length, Sha256(bytes));
        var noteId = Guid.CreateVersion7();
        var now = _clock.GetUtcNow();
        var push = new PushTasksCommandHandler(
            _replica,
            new CaptureAttachmentRelease(_store, new RecordingLogger()),
            NullLogger<PushTasksCommandHandler>.Instance);

        await push.Handle(new PushTasksCommand(Scope, [NoteChange(noteId, now, null, file)]), Cancellation);
        await push.Handle(new PushTasksCommand(Scope, [NoteChange(noteId, now.AddMinutes(1), null, file)]), Cancellation);

        Assert.NotNull(await _store.Find(Scope.OwnerId, fileId, Cancellation));

        await push.Handle(new PushTasksCommand(Scope, [NoteChange(noteId, now.AddMinutes(2), now.AddMinutes(2), file)]), Cancellation);

        Assert.Null(await _store.Find(Scope.OwnerId, fileId, Cancellation));
    }

    /// <summary>
    /// The replica writes a batch several documents at once, so a push that
    /// fails on one document can already have written others — here the note's
    /// tombstone. Its files are released all the same, and the device still
    /// hears the failure the replica raised, unchanged: a retry finds the note
    /// already a tombstone and would never release them.
    /// </summary>
    [Fact]
    public async Task A_push_that_fails_part_way_still_releases_the_files_of_a_tombstone_that_landed()
    {
        var (noteId, file) = await LiveNoteWithFile();
        var busy = new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "The task replica is busy. Try again shortly.");
        var push = new PushTasksCommandHandler(
            new PartlyWrittenReplica(_replica, busy),
            new CaptureAttachmentRelease(_store, new RecordingLogger()),
            NullLogger<PushTasksCommandHandler>.Instance);
        var at = _clock.GetUtcNow().AddMinutes(1);

        var thrown = await Assert.ThrowsAsync<SyncReplicaException>(
            () => push.Handle(new PushTasksCommand(Scope, [NoteChange(noteId, at, at, file)]), Cancellation));

        Assert.Same(busy, thrown);
        Assert.Null(await _store.Find(Scope.OwnerId, file.Id, Cancellation));
    }

    /// <summary>The release after a failed push is best effort: when the replica
    /// cannot even be read back, the device hears the push's own failure, not
    /// the read's.</summary>
    [Fact]
    public async Task A_release_that_fails_after_a_failed_push_does_not_hide_the_push_s_failure()
    {
        var (noteId, file) = await LiveNoteWithFile();
        var busy = new SyncReplicaException(SyncErrorCodes.ReplicaBusy, "The task replica is busy. Try again shortly.");
        var push = new PushTasksCommandHandler(
            new PartlyWrittenReplica(_replica, busy, unreadableAfterward: true),
            new CaptureAttachmentRelease(_store, new RecordingLogger()),
            NullLogger<PushTasksCommandHandler>.Instance);
        var at = _clock.GetUtcNow().AddMinutes(1);

        var thrown = await Assert.ThrowsAsync<SyncReplicaException>(
            () => push.Handle(new PushTasksCommand(Scope, [NoteChange(noteId, at, at, file)]), Cancellation));

        Assert.Same(busy, thrown);
    }

    private async Task<(Guid NoteId, AttachmentMetadata File)> LiveNoteWithFile()
    {
        var bytes = Bytes(32);
        var fileId = Guid.CreateVersion7();
        await Upload(fileId, bytes, maxBytes: 1_000);
        var file = new AttachmentMetadata(fileId, "slide.png", "image/png", bytes.Length, Sha256(bytes));
        var noteId = Guid.CreateVersion7();
        await _replica.Upsert(Scope, [NoteChange(noteId, _clock.GetUtcNow(), null, file)], Cancellation);

        return (noteId, file);
    }

    private static TaskChange NoteChange(Guid id, DateTimeOffset at, DateTimeOffset? deletedAt, AttachmentMetadata file) =>
        new(id, at, deletedAt, new TaskPayload(
            "Keynote", string.Empty, "note", "draft", "medium", 0, null, at, "mobile", null, null, null, null, null, null, null,
            null, null, null, [], [], [], [], [], [], Attachments: [file]));

    /// <summary>The delete failing is logged and left to the lifecycle rule;
    /// the acknowledgement succeeds and the tombstone is written.</summary>
    [Fact]
    public async Task An_acknowledgement_succeeds_and_logs_when_the_release_fails()
    {
        var capture = await CaptureWithAttachment();
        var logger = new RecordingLogger();

        var result = await Acknowledge(capture.Id, new CaptureAttachmentRelease(new DeleteFailingStore(_store), logger));

        Assert.True(result.IsSuccess);
        Assert.NotNull((await _replica.Find(Scope.OwnerId, capture.Id, Cancellation))!.Change.DeletedAt);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Levels));
    }

    private async Task<Result<StoreAttachmentOutcome>> Upload(Guid id, byte[] bytes, long maxBytes, string? sha256 = null) =>
        await new StoreAttachmentCommandHandler(_store).Handle(
            new StoreAttachmentCommand(Scope, id, "image/png", sha256 ?? Sha256(bytes), new MemoryStream(bytes), maxBytes),
            Cancellation);

    private Task<Result<CaptureOutcome>> Capture(CaptureInboxItemCommand command) =>
        new CaptureInboxItemCommandHandler(_replica, _store, _clock).Handle(command, Cancellation);

    private Task<Result> Acknowledge(Guid id, CaptureAttachmentRelease release) =>
        new AcknowledgeInboxItemCommandHandler(_replica, release, _clock).Handle(new AcknowledgeInboxItemCommand(Scope, id), Cancellation);

    private async Task<InboxItem> CaptureWithAttachment()
    {
        var bytes = Bytes(32);
        var id = Guid.CreateVersion7();
        await Upload(id, bytes, maxBytes: 1_000);

        var result = await Capture(new CaptureInboxItemCommand(
            Scope, "Keynote", "phone", Attachments: [new AttachmentMetadata(id, "slide.png", "image/png", bytes.Length, Sha256(bytes))]));

        return result.Value.Item;
    }

    private static byte[] Bytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class UnreadableStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The body was read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The body was read.");
    }

    private sealed class DeleteFailingStore(IAttachmentStore inner) : IAttachmentStore
    {
        public Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => inner.Find(owner, id, cancellationToken);

        public Task<IStagedAttachment> Stage(OwnerId owner, Guid id, Stream content, CancellationToken cancellationToken = default) => inner.Stage(owner, id, content, cancellationToken);

        public Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default) => inner.Open(owner, id, cancellationToken);

        public Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
            throw new SyncReplicaException(SyncErrorCodes.AttachmentStoreUnavailable, "The attachment store is not reachable yet.");
    }

    /// <summary>A replica that writes the whole batch and then fails, the way a
    /// concurrent write can land beside the one that failed. With
    /// <c>unreadableAfterward</c>, every read after that failure fails too.</summary>
    private sealed class PartlyWrittenReplica(ITaskReplica inner, Exception failure, bool unreadableAfterward = false) : ITaskReplica
    {
        private bool _failed;

        public async Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default)
        {
            await inner.Upsert(scope, changes, cancellationToken);
            _failed = true;
            throw failure;
        }

        public Task<TaskReplicaPage> ReadChanges(OwnerId owner, TaskReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
            inner.ReadChanges(owner, cursor, maxItems, cancellationToken);

        public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default) =>
            inner.ListCaptures(owner, cancellationToken);

        public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
            _failed && unreadableAfterward
                ? throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, "The task replica is not available yet. Try again shortly.")
                : inner.Find(owner, id, cancellationToken);
    }

    private sealed class RecordingLogger : ILogger<CaptureAttachmentRelease>
    {
        internal List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
