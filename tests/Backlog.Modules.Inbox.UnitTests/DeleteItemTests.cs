using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.DeleteItem;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Delete is not Archive: the item is gone, not kept. What survives a delete is
/// only what the phone is still owed — the acknowledgement that tells it to stop
/// offering a capture — and only until the outbox has sent it.
/// </summary>
public sealed class DeleteItemTests
{
    private static readonly DateTimeOffset Now = Items.Noon.AddMinutes(30);

    [Fact]
    public async Task Deleting_a_local_item_removes_it_and_leaves_nothing_behind()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Delete(store, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.Items);
        Assert.Empty(store.DeletedCaptures);
        Assert.Empty(await new InboxCaptureOutbox(store).ListPendingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Deleting_an_item_that_does_not_exist_is_not_found()
    {
        var result = await Delete(new InMemoryInboxStore(), Guid.CreateVersion7());

        Assert.Equal(InboxErrors.ItemNotFound, result.Error);
    }

    /// <summary>The acknowledgement is part of the change: an open item from the
    /// phone is one the phone is still offering, and it hears exactly as it
    /// would for an archive — a tombstone from the outbox.</summary>
    [Fact]
    public async Task Deleting_an_open_item_from_the_phone_leaves_its_acknowledgement_in_the_outbox()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone("Call the dentist");
        store.Seed(item);

        await Delete(store, item.Id);

        Assert.Empty(store.Items);
        var ack = Assert.Single(await new InboxCaptureOutbox(store).ListPendingAsync(TestContext.Current.CancellationToken));
        Assert.Equal(item.Id, ack.CaptureId);
        Assert.Equal("Call the dentist", ack.Title);
        Assert.Equal(Items.Noon, ack.CapturedAt);
        Assert.Equal(Now, ack.AcknowledgedAt);
    }

    [Fact]
    public async Task Once_the_outbox_sends_it_the_deleted_capture_is_forgotten()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        store.Seed(item);
        await Delete(store, item.Id);

        var outbox = new InboxCaptureOutbox(store);
        await outbox.MarkSentAsync([item.Id], TestContext.Current.CancellationToken);

        Assert.Empty(store.DeletedCaptures);
        Assert.Empty(await outbox.ListPendingAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An archived item whose tombstone has not left yet still owes
    /// it; deleting the item must not swallow that.</summary>
    [Fact]
    public async Task Deleting_a_decided_item_whose_acknowledgement_has_not_left_keeps_it()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        item.Archive(Items.Noon.AddMinutes(10));
        store.Seed(item);

        await Delete(store, item.Id);

        Assert.Single(store.DeletedCaptures);
    }

    [Fact]
    public async Task Deleting_a_phone_item_the_phone_already_heard_about_owes_it_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        item.Archive(Items.Noon.AddMinutes(10));
        item.MarkReplicaAcknowledged();
        store.Seed(item);

        await Delete(store, item.Id);

        Assert.Empty(store.Items);
        Assert.Empty(store.DeletedCaptures);
    }

    /// <summary>Until the phone has the tombstone it may send the capture
    /// again; that replay must not bring the deleted item back.</summary>
    [Fact]
    public async Task A_replay_of_a_deleted_capture_does_not_bring_it_back()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxCaptureDto(Guid.CreateVersion7(), "Call the dentist", "mobile", Items.Noon, Items.Noon, WithdrawnAt: null);
        await Receive(store, capture);
        await Delete(store, capture.Id);

        var outcome = await Receive(store, capture);

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Empty(store.Items);
        Assert.Single(store.DeletedCaptures);
    }

    /// <summary>The phone withdrawing the capture itself leaves nothing to tell
    /// it, so the acknowledgement goes without being sent.</summary>
    [Fact]
    public async Task The_phone_withdrawing_a_deleted_capture_forgets_its_acknowledgement()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxCaptureDto(Guid.CreateVersion7(), "Call the dentist", "mobile", Items.Noon, Items.Noon, WithdrawnAt: null);
        await Receive(store, capture);
        await Delete(store, capture.Id);

        var outcome = await Receive(store, capture with { WithdrawnAt = Now.AddMinutes(1) });

        Assert.Equal(InboxIntakeOutcome.Withdrawn, outcome);
        Assert.Empty(store.DeletedCaptures);
    }

    [Fact]
    public async Task Deleting_an_unrouted_item_removes_its_file_folder()
    {
        var store = new InMemoryInboxStore();
        var files = new FakeAttachmentFiles();
        var item = Items.FromPhone();
        item.RecordAttachments([InboxAttachment.Named(Guid.CreateVersion7(), "photo.jpg", "image/jpeg", 10, new string('a', 64))]);
        store.Seed(item);

        await Delete(store, item.Id, files);

        Assert.Equal([item.Id], files.RemovedFolders);
    }

    /// <summary>Routing handed the folder to the task as its attachment; the
    /// task still points there.</summary>
    [Fact]
    public async Task Deleting_a_routed_item_leaves_the_folder_its_task_points_at()
    {
        var store = new InMemoryInboxStore();
        var files = new FakeAttachmentFiles();
        var item = Items.FromPhone();
        item.RecordAttachments([InboxAttachment.Named(Guid.CreateVersion7(), "photo.jpg", "image/jpeg", 10, new string('a', 64))]);
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon.AddMinutes(10));
        store.Seed(item);

        await Delete(store, item.Id, files);

        Assert.Empty(store.Items);
        Assert.Empty(files.RemovedFolders);
    }

    private static Task<Backlog.SharedKernel.Results.Result> Delete(
        InMemoryInboxStore store,
        Guid id,
        FakeAttachmentFiles? files = null) =>
        new DeleteItemCommandHandler(store, new FakeTimeProvider(Now), files)
            .Handle(new DeleteItemCommand(id), TestContext.Current.CancellationToken);

    private static async Task<InboxIntakeOutcome> Receive(InMemoryInboxStore store, InboxCaptureDto capture)
    {
        var result = await new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Items.Noon.AddMinutes(5)))
            .Handle(new ReceiveCaptureCommand(capture), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
