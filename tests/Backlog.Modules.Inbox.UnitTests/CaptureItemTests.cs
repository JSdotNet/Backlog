using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.CaptureItem;
using Backlog.Modules.Inbox.Features.SetTags;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Results;

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

        var ok = Assert.IsType<Ok<InboxItemDto>>(result.Value);
        Assert.Equal(InboxEnumMap.ManualChannel, ok.Value.Channel);
        Assert.Equal(InboxStatus.Unprocessed, ok.Value.Status);
        Assert.Equal(ContentKind.Article, ok.Value.Kind);
        Assert.Equal("https://example.com/posts/local-first", ok.Value.SourceUrl);
        Assert.Equal(Items.Noon, ok.Value.CapturedAt);

        var stored = Assert.Single(store.Items.Values);
        Assert.False(stored.ReplicaBacked);
    }

    /// <summary>The Add dialog's notes are the item's body, trimmed; a capture
    /// without any is a bare one-line item rather than one with a blank body.</summary>
    [Fact]
    public async Task Notes_become_the_body_and_none_is_an_empty_body()
    {
        var store = new InMemoryInboxStore();
        var handler = new CaptureItemCommandHandler(store, Clock);

        var withNotes = await handler.Handle(
            new CaptureItemCommand("Ask about the trial length", "  Before Friday, and in writing.\n"),
            TestContext.Current.CancellationToken);
        var bare = await handler.Handle(
            new CaptureItemCommand("Just a title", "   "),
            TestContext.Current.CancellationToken);

        var withNotesOk = Assert.IsType<Ok<InboxItemDto>>(withNotes.Value);
        Assert.Equal("Before Friday, and in writing.", withNotesOk.Value.BodyMd);
        Assert.Equal(InboxEnumMap.ManualChannel, withNotesOk.Value.Channel);
        Assert.Equal(string.Empty, Assert.IsType<Ok<InboxItemDto>>(bare.Value).Value.BodyMd);
        Assert.Equal("Before Friday, and in writing.", store.Items[withNotesOk.Value.Id].BodyMd);
    }

    [Fact]
    public async Task A_blank_capture_needs_a_title()
    {
        var store = new InMemoryInboxStore();

        var result = await new CaptureItemCommandHandler(store, Clock)
            .Handle(new CaptureItemCommand("   "), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.item.needs_title", Assert.IsType<Error>(result.Value).Code);
        Assert.Empty(store.Items);
    }

    /// <summary>A source link named beside the title is the item's link, and the
    /// kind is read off it rather than off the words — an assistant's capture
    /// over MCP names one this way (<c>capture_to_inbox</c>).</summary>
    [Fact]
    public async Task A_named_source_link_is_the_items_link_and_its_kind()
    {
        var store = new InMemoryInboxStore();

        var result = await new CaptureItemCommandHandler(store, Clock)
            .Handle(
                new CaptureItemCommand("Watch the keynote", SourceUrl: "  https://www.youtube.com/watch?v=abc  "),
                TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<InboxItemDto>>(result.Value);
        Assert.Equal("https://www.youtube.com/watch?v=abc", ok.Value.SourceUrl);
        Assert.Equal(ContentKind.YouTube, ok.Value.Kind);
        Assert.Equal(InboxEnumMap.ManualChannel, ok.Value.Channel);
    }

    [Theory]
    [InlineData("not a link")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    public async Task A_source_link_that_is_no_web_address_is_refused(string sourceUrl)
    {
        var store = new InMemoryInboxStore();

        var result = await new CaptureItemCommandHandler(store, Clock)
            .Handle(new CaptureItemCommand("Something", SourceUrl: sourceUrl), TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.SourceUrlNotALink.Code, Assert.IsType<Error>(result.Value).Code);
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

        Assert.Equal("inbox.tag.person_not_a_tag", Assert.IsType<Error>(result.Value).Code);
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

        Assert.Equal("inbox.item.invalid_transition", Assert.IsType<Error>(result.Value).Code);
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
