using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.CaptureItem;
using Backlog.Modules.Inbox.Features.SetTags;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The manual capture path — the one that needs no phone — and the two small
/// item edits whose refusals a screen has to show as messages rather than
/// crashes.
/// </summary>
public sealed class CaptureItemTests
{
    private static readonly FakeTimeProvider Clock = new(Items.Noon);

    [Fact]
    public async Task A_typed_thought_becomes_an_unprocessed_manual_item()
    {
        var store = new InMemoryInboxStore();

        var result = await new CaptureItemCommandHandler(store, Clock)
            .Handle(new CaptureItemCommand("Read this https://example.com/posts/local-first"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxEnumMap.ManualChannel, result.Value.Channel);
        Assert.Equal(InboxStatus.Unprocessed, result.Value.Status);
        Assert.Equal(ContentKind.Article, result.Value.Kind);
        Assert.Equal("https://example.com/posts/local-first", result.Value.SourceUrl);
        Assert.Equal(Items.Noon, result.Value.CapturedAt);

        var stored = Assert.Single(store.Items.Values);
        Assert.False(stored.ReplicaBacked);
    }

    [Fact]
    public async Task A_blank_capture_needs_a_title()
    {
        var store = new InMemoryInboxStore();

        var result = await new CaptureItemCommandHandler(store, Clock)
            .Handle(new CaptureItemCommand("   "), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.item.needs_title", result.Error.Code);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task A_person_among_the_tags_is_a_validation_message()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await new SetTagsCommandHandler(store)
            .Handle(new SetTagsCommand(item.Id, ["deploy", "@bob"]), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.tag.person_not_a_tag", result.Error.Code);
        Assert.Empty(item.Tags);
    }

    [Fact]
    public async Task Archiving_a_routed_item_is_a_validation_message()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(item);

        var result = await new ArchiveItemCommandHandler(store, Clock)
            .Handle(new ArchiveItemCommand(item.Id), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Equal(InboxStatus.Triaged, item.Status);
    }

    /// <summary>The outbox reads the flag and clears it; a manual item is never
    /// in it, and clearing an id that is not there is not an error.</summary>
    [Fact]
    public async Task The_outbox_lists_only_replica_items_that_owe_an_acknowledgement()
    {
        var store = new InMemoryInboxStore();
        var phone = Items.FromPhone("From the phone");
        phone.Archive(Items.Noon.AddHours(1));
        var manual = Items.Manual("Typed here");
        manual.Archive(Items.Noon.AddHours(1));
        store.Seed(phone);
        store.Seed(manual);

        var outbox = new InboxCaptureOutbox(store);

        var pending = Assert.Single(await outbox.ListPendingAsync(TestContext.Current.CancellationToken));
        Assert.Equal(phone.Id, pending.CaptureId);
        Assert.Equal("From the phone", pending.Title);
        Assert.Equal(Items.Noon, pending.CapturedAt);
        Assert.Equal(Items.Noon.AddHours(1), pending.AcknowledgedAt);

        await outbox.MarkSentAsync([phone.Id, manual.Id, Guid.CreateVersion7()], TestContext.Current.CancellationToken);

        Assert.False(phone.ReplicaAckPending);
        Assert.Empty(await outbox.ListPendingAsync(TestContext.Current.CancellationToken));
    }
}
