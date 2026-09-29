using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.CreatePlan;
using Backlog.Modules.Inbox.Features.OpenAttachment;
using Backlog.Modules.Inbox.Features.ReadAttachment;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.Modules.Inbox.Features.RetryAttachment;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// A capture that names files becomes an item that carries them (local ADR
/// 0014): the metadata is recorded before anything is fetched, each file is
/// fetched into the item's folder once, a fetch that fails is recorded on the
/// file without failing the capture, and a replayed page fetches nothing twice.
/// </summary>
public sealed class AttachmentIntakeTests
{
    private static readonly DateTimeOffset Arrival = Items.Noon.AddMinutes(10);

    [Fact]
    public async Task The_files_metadata_is_recorded_on_the_item()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();

        var outcome = await Receive(store, Capture(jpeg, pdf), new FakeAttachmentSource().Holding(jpeg, pdf), new FakeAttachmentFiles());

        Assert.Equal(InboxIntakeOutcome.Received, outcome);

        var item = Assert.Single(store.Items.Values);
        Assert.Collection(
            item.Attachments,
            first =>
            {
                Assert.Equal(jpeg.Id, first.Id);
                Assert.Equal("whiteboard.jpg", first.Name);
                Assert.Equal("image/jpeg", first.ContentType);
                Assert.Equal(jpeg.Bytes.Length, first.SizeBytes);
                Assert.Equal(jpeg.Sha256, first.Sha256);
            },
            second => Assert.Equal(pdf.Id, second.Id));
    }

    [Fact]
    public async Task The_metadata_is_saved_before_any_file_is_fetched()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var source = new FakeAttachmentSource().Holding(jpeg);
        var capture = Capture(jpeg);

        // The first save has to be the item with its file named and not yet on
        // disk: that is what keeps the item in the inbox if the fetch hangs.
        var recordedBeforeFetch = false;
        var files = new SavingOrderFiles(store, capture.Id, () => recordedBeforeFetch = true);

        await Receive(store, capture, source, files);

        Assert.True(recordedBeforeFetch);
        Assert.Equal([capture.Id, capture.Id], store.ItemWrites);
    }

    [Fact]
    public async Task Each_file_is_written_into_the_items_folder_and_marked_downloaded()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();
        var files = new FakeAttachmentFiles();

        await Receive(store, Capture(jpeg, pdf), new FakeAttachmentSource().Holding(jpeg, pdf), files);

        var item = Assert.Single(store.Items.Values);
        Assert.All(item.Attachments, attachment =>
        {
            Assert.True(attachment.IsDownloaded);
            Assert.Equal(Arrival, attachment.DownloadedAt);
            Assert.Null(attachment.LastError);
        });

        Assert.Equal($"inbox/attachments/{item.Id:D}/whiteboard.jpg", item.Attachments[0].LocalPath);
        Assert.Equal(jpeg.Bytes, files.Files[item.Attachments[0].LocalPath!]);
        Assert.Equal(pdf.Bytes, files.Files[item.Attachments[1].LocalPath!]);
    }

    [Fact]
    public async Task A_replayed_page_fetches_and_writes_nothing_twice()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var source = new FakeAttachmentSource().Holding(jpeg);
        var files = new FakeAttachmentFiles();
        var capture = Capture(jpeg);

        await Receive(store, capture, source, files);
        var writesAfterFirst = store.ItemWrites.Count;

        var outcome = await Receive(store, capture, source, files);

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Equal([jpeg.Id], source.Fetches);
        Assert.Single(files.Writes);
        Assert.Equal(writesAfterFirst, store.ItemWrites.Count);
    }

    [Fact]
    public async Task A_file_already_on_disk_with_the_right_digest_is_not_fetched()
    {
        // A crash between writing the file and saving the item: the file is
        // there, the row still says waiting. The next pass finds it rather than
        // fetching it again.
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var source = new FakeAttachmentSource().Holding(jpeg);
        var files = new FakeAttachmentFiles();
        var capture = Capture(jpeg);

        files.Files[files.PathFor(capture.Id, "whiteboard.jpg")] = jpeg.Bytes;

        await Receive(store, capture, source, files);

        Assert.Empty(source.Fetches);
        Assert.Empty(files.Writes);
        Assert.True(Assert.Single(Assert.Single(store.Items.Values).Attachments).IsDownloaded);
    }

    [Fact]
    public async Task A_failed_fetch_is_recorded_on_the_file_and_the_capture_still_lands()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();

        // The PDF has aged out of the store; the JPEG is still there.
        var outcome = await Receive(store, Capture(jpeg, pdf), new FakeAttachmentSource().Holding(jpeg), new FakeAttachmentFiles());

        Assert.Equal(InboxIntakeOutcome.Received, outcome);

        var item = Assert.Single(store.Items.Values);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.True(item.Attachments[0].IsDownloaded);

        var failed = item.Attachments[1];
        Assert.False(failed.IsDownloaded);
        Assert.Null(failed.LocalPath);
        Assert.Equal("The file is no longer on the sync service.", failed.LastError);
    }

    [Fact]
    public async Task A_fetch_that_throws_is_recorded_rather_than_thrown()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var source = new FakeAttachmentSource { ThrowWith = new HttpRequestException("Connection reset.") };

        var outcome = await Receive(store, Capture(jpeg), source, new FakeAttachmentFiles());

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
        Assert.Equal(
            "The file could not be downloaded: Connection reset.",
            Assert.Single(Assert.Single(store.Items.Values).Attachments).LastError);
    }

    [Fact]
    public async Task A_file_fetched_but_not_saved_says_this_machine_could_not_keep_it()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var files = new FakeAttachmentFiles { ThrowOnWrite = new UnauthorizedAccessException("Access to the path is denied.") };

        var outcome = await Receive(store, Capture(jpeg), new FakeAttachmentSource().Holding(jpeg), files);

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
        Assert.Equal(
            "The file could not be saved on this machine: Access to the path is denied.",
            Assert.Single(Assert.Single(store.Items.Values).Attachments).LastError);
    }

    [Fact]
    public async Task Bytes_that_do_not_match_the_captures_digest_are_not_kept()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var source = new FakeAttachmentSource();
        source.Blobs[jpeg.Id] = [9, 9, 9];
        var files = new FakeAttachmentFiles();

        await Receive(store, Capture(jpeg), source, files);

        Assert.Empty(files.Writes);
        Assert.Contains("does not match", Assert.Single(Assert.Single(store.Items.Values).Attachments).LastError);
    }

    [Fact]
    public async Task A_replay_does_not_retry_a_failed_file_but_records_one_it_lacked()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();
        var source = new FakeAttachmentSource();
        var files = new FakeAttachmentFiles();

        await Receive(store, Capture(jpeg), source, files);
        source.Holding(jpeg, pdf);

        // The same capture again, naming a second file — an item made before
        // this build read files arrives like this.
        await Receive(store, Capture(jpeg, pdf) with { Id = store.Items.Keys.Single() }, source, files);

        var item = Assert.Single(store.Items.Values);
        Assert.Equal([jpeg.Id, pdf.Id], source.Fetches);
        Assert.NotNull(item.Attachments[0].LastError);
        Assert.True(item.Attachments[1].IsDownloaded);
    }

    [Fact]
    public async Task Without_a_source_the_files_are_recorded_and_left_waiting()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();

        var handler = new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival));
        var result = await handler.Handle(new ReceiveCaptureCommand(Capture(jpeg)), TestContext.Current.CancellationToken);

        Assert.Equal(InboxIntakeOutcome.Received, result.Value);
        var attachment = Assert.Single(Assert.Single(store.Items.Values).Attachments);
        Assert.True(attachment.IsWaiting);
    }

    [Fact]
    public async Task A_named_file_no_fetch_could_honour_is_dropped_not_thrown()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var capture = Capture(jpeg) with
        {
            Attachments = [jpeg.Named, new InboxCaptureAttachmentDto(Guid.CreateVersion7(), "x.bin", "application/octet-stream", 3, "not-a-digest")],
        };

        var outcome = await Receive(store, capture, new FakeAttachmentSource().Holding(jpeg), new FakeAttachmentFiles());

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
        Assert.Equal(jpeg.Id, Assert.Single(Assert.Single(store.Items.Values).Attachments).Id);
    }

    [Fact]
    public async Task A_picture_and_nothing_else_arrives_as_an_image_and_a_file_as_a_document()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();

        var photo = Capture(jpeg) with { Title = "IMG_2041" };
        var contract = Capture(pdf) with { Id = Guid.CreateVersion7(), Title = "Contract to sign" };

        await Receive(store, photo, new FakeAttachmentSource().Holding(jpeg), new FakeAttachmentFiles());
        await Receive(store, contract, new FakeAttachmentSource().Holding(pdf), new FakeAttachmentFiles());

        Assert.Equal(ContentKind.Image, store.Items[photo.Id].Kind);
        Assert.Equal(ContentKind.Document, store.Items[contract.Id].Kind);
    }

    // --- Retry, read, open ---------------------------------------------------

    [Fact]
    public async Task Retry_fetches_a_failed_file_and_clears_its_error()
    {
        var store = new InMemoryInboxStore();
        var pdf = TestFile.Pdf();
        var source = new FakeAttachmentSource();
        var files = new FakeAttachmentFiles();
        var capture = Capture(pdf);

        await Receive(store, capture, source, files);
        source.Holding(pdf);

        var result = await new RetryAttachmentCommandHandler(store, new FakeTimeProvider(Arrival), source, files)
            .Handle(new RetryAttachmentCommand(capture.Id, pdf.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var attachment = Assert.Single(store.Items[capture.Id].Attachments);
        Assert.True(attachment.IsDownloaded);
        Assert.Null(attachment.LastError);
    }

    [Fact]
    public async Task Retry_that_fails_again_answers_the_reason_and_records_it()
    {
        var store = new InMemoryInboxStore();
        var pdf = TestFile.Pdf();
        var source = new FakeAttachmentSource();
        var capture = Capture(pdf);

        await Receive(store, capture, source, new FakeAttachmentFiles());
        source.FailWith = Error.Unexpected("sync.unreachable", "The sync service could not be reached.");

        var result = await new RetryAttachmentCommandHandler(store, new FakeTimeProvider(Arrival), source, new FakeAttachmentFiles())
            .Handle(new RetryAttachmentCommand(capture.Id, pdf.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("The sync service could not be reached.", result.Error.Message);
        Assert.Equal(result.Error.Message, Assert.Single(store.Items[capture.Id].Attachments).LastError);
    }

    [Fact]
    public async Task Retry_without_the_ports_says_attachments_are_unavailable()
    {
        var store = new InMemoryInboxStore();
        var pdf = TestFile.Pdf();
        var capture = Capture(pdf);
        await Receive(store, capture, new FakeAttachmentSource(), new FakeAttachmentFiles());

        var result = await new RetryAttachmentCommandHandler(store, new FakeTimeProvider(Arrival))
            .Handle(new RetryAttachmentCommand(capture.Id, pdf.Id), TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.AttachmentsUnavailable, result.Error);
    }

    [Fact]
    public async Task A_downloaded_file_can_be_read_and_opened_and_a_waiting_one_cannot()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();
        var files = new FakeAttachmentFiles();
        var capture = Capture(jpeg, pdf);

        await Receive(store, capture, new FakeAttachmentSource().Holding(jpeg), files);

        var read = await new ReadAttachmentQueryHandler(store, files)
            .Handle(new ReadAttachmentQuery(capture.Id, jpeg.Id), TestContext.Current.CancellationToken);
        var opened = await new OpenAttachmentCommandHandler(store, files)
            .Handle(new OpenAttachmentCommand(capture.Id, jpeg.Id), TestContext.Current.CancellationToken);
        var notThere = await new ReadAttachmentQueryHandler(store, files)
            .Handle(new ReadAttachmentQuery(capture.Id, pdf.Id), TestContext.Current.CancellationToken);

        Assert.Equal(jpeg.Bytes, read.Value);
        Assert.True(opened.IsSuccess);
        Assert.Equal([store.Items[capture.Id].Attachments[0].LocalPath!], files.OpenedPaths);
        Assert.Equal(InboxErrors.AttachmentNotDownloaded, notThere.Error);
    }

    // --- Routing ------------------------------------------------------------

    [Fact]
    public async Task Routing_hands_the_items_folder_to_the_task_as_its_attachment()
    {
        var store = new InMemoryInboxStore();
        var pdf = TestFile.Pdf();
        var files = new FakeAttachmentFiles();
        var capture = Capture(pdf);
        await Receive(store, capture, new FakeAttachmentSource().Holding(pdf), files);

        var target = new FakeBacklogTarget();
        await new RouteToBacklogCommandHandler(store, target, new FakeTimeProvider(Arrival), files)
            .Handle(new RouteToBacklogCommand(capture.Id), TestContext.Current.CancellationToken);

        Assert.Equal(files.FolderFor(capture.Id), Assert.Single(target.Requests).AttachmentPath);
    }

    [Fact]
    public async Task Routing_an_item_without_files_hands_on_no_attachment()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        store.Seed(item);

        var target = new FakeBacklogTarget();
        await new RouteToBacklogCommandHandler(store, target, new FakeTimeProvider(Arrival), new FakeAttachmentFiles())
            .Handle(new RouteToBacklogCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(target.Requests).AttachmentPath);
    }

    [Fact]
    public async Task Creating_a_plan_hands_the_items_folder_to_the_import_as_its_attachment()
    {
        var store = new InMemoryInboxStore();
        var jpeg = TestFile.Jpeg();
        var pdf = TestFile.Pdf();
        var files = new FakeAttachmentFiles();
        var capture = Capture(jpeg, pdf);
        await Receive(store, capture, new FakeAttachmentSource().Holding(jpeg, pdf), files);

        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter();
        await new CreatePlanCommandHandler(store, target, new FakeTimeProvider(Arrival), drafter, files)
            .Handle(new CreatePlanCommand(capture.Id), TestContext.Current.CancellationToken);

        Assert.Equal(files.FolderFor(capture.Id), Assert.Single(target.Imports).AttachmentPath);
        Assert.DoesNotContain(files.FolderFor(capture.Id), Assert.Single(drafter.Requests).BodyMd, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creating_a_plan_from_an_item_without_files_hands_on_no_attachment()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        store.Seed(item);

        var target = new FakeBacklogTarget();
        await new CreatePlanCommandHandler(store, target, new FakeTimeProvider(Arrival), new FakePlanDrafter(), new FakeAttachmentFiles())
            .Handle(new CreatePlanCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(target.Imports).AttachmentPath);
    }

    // --- Helpers --------------------------------------------------------------

    private static InboxCaptureDto Capture(params TestFile[] files) =>
        new(
            Guid.CreateVersion7(),
            "Whiteboard from the planning session",
            "mobile",
            Items.Noon,
            Items.Noon,
            WithdrawnAt: null,
            Attachments: [.. files.Select(file => file.Named)]);

    private static async Task<InboxIntakeOutcome> Receive(
        InMemoryInboxStore store,
        InboxCaptureDto capture,
        IInboxAttachmentSource source,
        IInboxAttachmentFiles files)
    {
        var handler = new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival), source, files);
        var result = await handler.Handle(new ReceiveCaptureCommand(capture), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>A file store that, on the first write, checks the store
    /// already holds the item with its file named and not yet downloaded.</summary>
    private sealed class SavingOrderFiles(InMemoryInboxStore store, Guid itemId, Action recorded) : IInboxAttachmentFiles
    {
        private readonly FakeAttachmentFiles _inner = new();

        public string FolderFor(Guid id) => _inner.FolderFor(id);

        public string PathFor(Guid id, string fileName) => _inner.PathFor(id, fileName);

        public Task<string> WriteAsync(Guid id, string fileName, byte[] content, CancellationToken cancellationToken = default)
        {
            if (store.Items.TryGetValue(itemId, out var saved) && saved.Attachments.Count == 1)
            {
                recorded();
            }

            return _inner.WriteAsync(id, fileName, content, cancellationToken);
        }

        public Task<bool> HoldsAsync(string path, string sha256, CancellationToken cancellationToken = default) =>
            _inner.HoldsAsync(path, sha256, cancellationToken);

        public Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(path, cancellationToken);

        public Result Open(string path) => _inner.Open(path);

        public Task RemoveFolderAsync(Guid id, CancellationToken cancellationToken = default) =>
            _inner.RemoveFolderAsync(id, cancellationToken);
    }
}
