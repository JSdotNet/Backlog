using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// What a replica capture means to this machine, by id and by status. The four
/// outcomes are the four combinations of "known here" and "withdrawn there",
/// and the one with a side effect — a tombstone archiving an unprocessed item —
/// must not leave an acknowledgement behind, because the replica is where the
/// tombstone came from.
/// </summary>
public sealed class ReceiveCaptureTests
{
    private static readonly DateTimeOffset Arrival = Items.Noon.AddMinutes(10);

    [Fact]
    public async Task A_new_live_capture_becomes_an_item_with_the_captures_id()
    {
        var store = new InMemoryInboxStore();
        var capture = Capture("Aspire 13 walkthrough https://www.youtube.com/watch?v=abc", channel: "mobile");

        var outcome = await Receive(store, capture);

        Assert.Equal(InboxIntakeOutcome.Received, outcome);

        var item = Assert.Single(store.Items.Values);
        Assert.Equal(capture.Id, item.Id);
        Assert.True(item.ReplicaBacked);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Equal(ContentKind.YouTube, item.Kind);
        Assert.Equal("https://www.youtube.com/watch?v=abc", item.SourceUrl);
        Assert.Equal("mobile", item.Source.Channel);
        Assert.Equal(Items.Noon, item.CapturedAt);
        Assert.Equal(Arrival, item.ReceivedAt);
    }

    [Fact]
    public async Task The_same_capture_arriving_again_is_already_known()
    {
        var store = new InMemoryInboxStore();
        var capture = Capture("Call the dentist");

        await Receive(store, capture);
        var outcome = await Receive(store, capture);

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Single(store.Items);
        Assert.Single(store.ItemWrites);
    }

    [Fact]
    public async Task A_tombstone_for_an_unprocessed_item_archives_it_without_owing_an_acknowledgement()
    {
        var store = new InMemoryInboxStore();
        var capture = Capture("Call the dentist");
        await Receive(store, capture);

        var outcome = await Receive(store, capture with { WithdrawnAt = Items.Noon.AddMinutes(20) });

        Assert.Equal(InboxIntakeOutcome.Withdrawn, outcome);

        var item = store.Items[capture.Id];
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.False(item.ReplicaAckPending);
    }

    /// <summary>The desktop's own tombstone coming back round after it routed
    /// the item: nothing to do, and above all not an archive over a routing.</summary>
    [Fact]
    public async Task A_tombstone_for_an_item_this_desktop_already_decided_on_is_already_known()
    {
        var store = new InMemoryInboxStore();
        var capture = Capture("Call the dentist");
        await Receive(store, capture);

        store.Items[capture.Id].RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon.AddMinutes(15));

        var outcome = await Receive(store, capture with { WithdrawnAt = Items.Noon.AddMinutes(20) });

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Equal(InboxStatus.Triaged, store.Items[capture.Id].Status);
        Assert.True(store.Items[capture.Id].IsRouted);
    }

    /// <summary>A deferred item is still open — put aside, not decided — so the
    /// phone dismissing the thought closes it here the same way it closes an
    /// unprocessed one.</summary>
    [Fact]
    public async Task A_tombstone_for_a_deferred_item_archives_it_too()
    {
        var store = new InMemoryInboxStore();
        var capture = Capture("Call the dentist");
        await Receive(store, capture);
        store.Items[capture.Id].Defer(until: null, Items.Noon.AddMinutes(15));

        var outcome = await Receive(store, capture with { WithdrawnAt = Items.Noon.AddMinutes(20) });

        Assert.Equal(InboxIntakeOutcome.Withdrawn, outcome);
        Assert.Equal(InboxStatus.Archived, store.Items[capture.Id].Status);
        Assert.False(store.Items[capture.Id].ReplicaAckPending);
    }

    /// <summary>The push endpoint validates nothing, so a capture with no title
    /// can arrive. There is no item to make of it, and the aggregate would say
    /// so by throwing — which, from the sync client's side, would pin the pull
    /// on this page for ever. Ignored, and the page moves on.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_live_capture_with_no_title_is_ignored_rather_than_refused(string title)
    {
        var store = new InMemoryInboxStore();

        var outcome = await Receive(store, Capture(title));

        Assert.Equal(InboxIntakeOutcome.Ignored, outcome);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task A_tombstone_for_an_unknown_id_is_ignored()
    {
        var store = new InMemoryInboxStore();

        var outcome = await Receive(store, Capture("Never seen") with { WithdrawnAt = Items.Noon });

        Assert.Equal(InboxIntakeOutcome.Ignored, outcome);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task The_editor_extensions_channel_is_filed_as_ide()
    {
        var store = new InMemoryInboxStore();

        await Receive(store, Capture("From the editor", channel: "vscode"));

        Assert.Equal("ide", Assert.Single(store.Items.Values).Source.Channel);
    }

    [Fact]
    public async Task A_channel_nobody_knows_is_kept_as_written()
    {
        var store = new InMemoryInboxStore();

        await Receive(store, Capture("From somewhere", channel: "watch"));

        Assert.Equal("watch", Assert.Single(store.Items.Values).Source.Channel);
    }

    /// <summary>The published port unwraps the handler's result: the caller is
    /// the sync client, and every capture has an outcome.</summary>
    [Fact]
    public async Task The_intake_port_answers_with_the_outcome_alone()
    {
        var store = new InMemoryInboxStore();
        var intake = new InboxIntake(new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival)));

        var outcome = await intake.ReceiveAsync(Capture("Call the dentist"), TestContext.Current.CancellationToken);

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
    }

    private static InboxCaptureDto Capture(string title, string channel = "mobile") =>
        new(Guid.CreateVersion7(), title, channel, Items.Noon, Items.Noon, WithdrawnAt: null);

    private static async Task<InboxIntakeOutcome> Receive(InMemoryInboxStore store, InboxCaptureDto capture)
    {
        var handler = new ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival));
        var result = await handler.Handle(new ReceiveCaptureCommand(capture), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
