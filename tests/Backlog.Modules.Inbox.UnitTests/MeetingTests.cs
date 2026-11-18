using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.EditNote;
using Backlog.Modules.Inbox.Features.ListPendingNotes;
using Backlog.Modules.Inbox.Features.ReceiveNote;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// A meeting: a note the phone starts with a recording running
/// (.devbook/domain/inbox/domain.md#meeting). It syncs, edits and is triaged as a
/// note does, keeps its own kind both ways, and a note that is later given a
/// recording stays a note (.devbook/domain/inbox/requirements.md#meetings-on-the-phone).
/// </summary>
public sealed class MeetingTests
{
    private static readonly DateTimeOffset Arrival = Items.Noon.AddMinutes(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_meeting_from_the_phone_reaches_the_desktop_as_a_meeting()
    {
        var store = new InMemoryInboxStore();
        var meeting = Meeting("Weekly sync", Items.Noon);

        var outcome = await Receive(store, meeting);

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
        var item = Assert.Single(store.Items.Values);
        Assert.Equal(ContentKind.Meeting, item.Kind);
        Assert.Equal("meeting", item.KindSlug);
        Assert.False(item.NotePushPending);
    }

    [Fact]
    public async Task A_meeting_is_edited_and_pushed_back_as_a_meeting()
    {
        var store = new InMemoryInboxStore();
        var meeting = Meeting("Weekly sync", Items.Noon);
        await Receive(store, meeting);

        var edited = await new EditNoteCommandHandler(store, new FakeTimeProvider(Arrival))
            .Handle(new EditNoteCommand(meeting.Id, "Weekly sync", "decided to ship"), Cancellation);

        Assert.True(edited.IsSuccess);
        var pending = await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation);
        var pushed = Assert.Single(pending.Value);
        Assert.Equal("decided to ship", pushed.BodyMd);
        Assert.Equal("meeting", pushed.Kind);
    }

    [Fact]
    public async Task A_note_pushed_back_says_it_is_a_note()
    {
        var store = new InMemoryInboxStore();
        var note = Meeting("Standup", Items.Noon) with { Kind = "note" };
        await Receive(store, note);
        store.Items[note.Id].EditNote("Standup", "desk", Arrival);

        var pending = await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation);

        Assert.Equal("note", Assert.Single(pending.Value).Kind);
    }

    [Fact]
    public async Task A_note_given_a_recording_later_stays_a_note()
    {
        var store = new InMemoryInboxStore();
        var note = Meeting("Standup", Items.Noon) with { Kind = "note" };
        await Receive(store, note);

        // A later copy claiming another kind changes the text, never the kind: an
        // item is a meeting only because it was started as one.
        var later = await Receive(store, note with { BodyMd = "recorded", UpdatedAt = Items.Noon.AddMinutes(5), Kind = "meeting" });

        Assert.Equal(InboxIntakeOutcome.Updated, later);
        Assert.Equal(ContentKind.Note, store.Items[note.Id].Kind);
    }

    [Fact]
    public async Task A_note_document_of_a_kind_that_is_not_a_note_kind_is_taken_in_as_a_note()
    {
        var store = new InMemoryInboxStore();

        await Receive(store, Meeting("Odd", Items.Noon) with { Kind = "article" });

        Assert.Equal(ContentKind.Note, Assert.Single(store.Items.Values).Kind);
    }

    [Fact]
    public async Task Archiving_a_meeting_owes_the_phone_a_tombstone_that_travels_as_a_note_document()
    {
        var store = new InMemoryInboxStore();
        var meeting = Meeting("Weekly sync", Items.Noon);
        await Receive(store, meeting);
        var item = store.Items[meeting.Id];

        item.Archive(Arrival);
        await store.SaveAsync(item, Cancellation);

        var ack = Assert.Single(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
        Assert.Equal(meeting.Id, ack.CaptureId);
        Assert.True(InboxEnumMap.IsNoteKind(ack.Kind));
    }

    [Fact]
    public void A_meeting_made_on_this_desktop_waits_to_be_pushed_as_a_note_does()
    {
        var item = InboxItem.Capture("Kept here", new InboxSource(InboxEnumMap.ManualChannel, null), null, ContentKind.Meeting, Items.Noon, Items.Noon, replicaBacked: false);

        Assert.True(item.IsNote);
        Assert.True(item.NotePushPending);
    }

    [Fact]
    public async Task A_capture_claiming_the_meeting_kind_stays_a_plain_thought()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxCaptureDto(Guid.CreateVersion7(), "Not really a meeting", "mobile", Items.Noon, Items.Noon, null, Kind: "meeting");

        await new InboxIntake(new Features.ReceiveCapture.ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival)))
            .ReceiveAsync(capture, Cancellation);

        Assert.Equal(ContentKind.Text, Assert.Single(store.Items.Values).Kind);
    }

    private static InboxNoteDto Meeting(string title, DateTimeOffset updatedAt) =>
        new(Guid.CreateVersion7(), title, null, "mobile", Items.Noon, updatedAt, Kind: "meeting");

    private static async Task<InboxIntakeOutcome> Receive(InMemoryInboxStore store, InboxNoteDto note)
    {
        var replication = new InboxNoteReplication(
            new ReceiveNoteCommandHandler(store, new FakeTimeProvider(Arrival)),
            new ListPendingNotesQueryHandler(store),
            new MarkNotesPushedCommandHandler(store));

        return await replication.ReceiveAsync(note, Cancellation);
    }
}
