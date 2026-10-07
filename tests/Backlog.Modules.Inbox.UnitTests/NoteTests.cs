using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.DeleteItem;
using Backlog.Modules.Inbox.Features.EditNote;
using Backlog.Modules.Inbox.Features.ListPendingNotes;
using Backlog.Modules.Inbox.Features.ReceiveNote;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// A note, the one Inbox item that syncs both ways with the phone
/// (.devbook/domain/inbox/domain.md#note, .devbook/arc42/06-runtime-view.md#mobile-note-sync):
/// it takes the later copy by its own stamp, it is never acknowledged away when it
/// is taken in or routed, archiving or deleting it owes the phone a note tombstone,
/// and only a note is edited after it is captured.
/// </summary>
public sealed class NoteTests
{
    private static readonly DateTimeOffset Arrival = Items.Noon.AddMinutes(10);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // --- Intake ---------------------------------------------------------------

    [Fact]
    public async Task A_new_note_from_the_phone_becomes_an_item_of_kind_note_under_its_id_and_stamp()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", "- shipped sync", Items.Noon);

        var outcome = await Receive(store, note);

        Assert.Equal(InboxIntakeOutcome.Received, outcome);
        var item = Assert.Single(store.Items.Values);
        Assert.Equal(note.Id, item.Id);
        Assert.Equal(ContentKind.Note, item.Kind);
        Assert.Equal(InboxEnumMap.NoteKind, item.KindSlug);
        Assert.Equal("- shipped sync", item.BodyMd);
        Assert.Equal("mobile", item.Source.Channel);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Equal(Items.Noon, item.EditedAt);

        // Taken in, not acknowledged away: nothing tells the phone to forget it.
        Assert.False(item.ReplicaAckPending);
        Assert.Empty(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
    }

    [Fact]
    public async Task A_later_copy_replaces_the_text_and_an_earlier_one_changes_nothing()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", "first", Items.Noon);
        await Receive(store, note);

        var later = await Receive(store, note with { Title = "Standup (phone)", BodyMd = "second", UpdatedAt = Items.Noon.AddMinutes(5) });
        var earlier = await Receive(store, note with { Title = "Stale", BodyMd = "old", UpdatedAt = Items.Noon.AddMinutes(1) });

        Assert.Equal(InboxIntakeOutcome.Updated, later);
        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, earlier);
        var item = store.Items[note.Id];
        Assert.Equal("Standup (phone)", item.Title);
        Assert.Equal("second", item.BodyMd);
        Assert.Equal(Items.Noon.AddMinutes(5), item.EditedAt);
    }

    [Fact]
    public async Task A_copy_naming_a_new_file_records_it_on_the_note()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Keynote", null, Items.Noon);
        await Receive(store, note);

        var file = new InboxCaptureAttachmentDto(Guid.CreateVersion7(), "slide.jpg", "image/jpeg", 12, new string('a', 64));
        await Receive(store, note with { UpdatedAt = Items.Noon.AddMinutes(2), Attachments = [file] });

        Assert.Equal(file.Id, Assert.Single(store.Items[note.Id].Attachments).Id);
    }

    [Fact]
    public async Task A_tombstone_from_another_desktop_archives_the_note_without_echoing_it()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", null, Items.Noon);
        await Receive(store, note);

        var outcome = await Receive(store, note with { UpdatedAt = Items.Noon.AddMinutes(3), DeletedAt = Items.Noon.AddMinutes(3) });

        Assert.Equal(InboxIntakeOutcome.Withdrawn, outcome);
        Assert.Equal(InboxStatus.Archived, store.Items[note.Id].Status);
        Assert.False(store.Items[note.Id].ReplicaAckPending);
    }

    [Fact]
    public async Task A_phone_edit_to_a_note_archived_here_restates_the_archive_under_a_later_stamp()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", null, Items.Noon);
        await Receive(store, note);
        var item = store.Items[note.Id];
        item.Archive(Items.Noon.AddMinutes(5));
        item.MarkReplicaAcknowledged();

        var outcome = await Receive(store, note with { BodyMd = "edited offline", UpdatedAt = Items.Noon.AddMinutes(7) });

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Equal(string.Empty, item.BodyMd);
        var ack = Assert.Single(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
        Assert.Equal(InboxEnumMap.NoteKind, ack.Kind);
        Assert.Equal(Arrival, ack.AcknowledgedAt);
    }

    [Fact]
    public async Task A_note_document_never_turns_an_item_of_another_kind_into_a_note()
    {
        var store = new InMemoryInboxStore();
        var capture = Items.FromPhone("A plain thought");
        store.Seed(capture);

        var outcome = await Receive(store, Note("Now a note?", "no", Items.Noon.AddHours(1), capture.Id));

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Equal(ContentKind.Text, store.Items[capture.Id].Kind);
        Assert.Equal("A plain thought", store.Items[capture.Id].Title);
    }

    [Fact]
    public async Task A_note_deleted_here_is_not_brought_back_by_the_phones_copy()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", null, Items.Noon);
        await Receive(store, note);
        await new DeleteItemCommandHandler(store, new FakeTimeProvider(Arrival)).Handle(new DeleteItemCommand(note.Id), Cancellation);

        var outcome = await Receive(store, note);

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task A_note_with_no_title_or_a_tombstone_for_an_unknown_id_is_ignored()
    {
        var store = new InMemoryInboxStore();

        Assert.Equal(InboxIntakeOutcome.Ignored, await Receive(store, Note("  ", "body", Items.Noon)));
        Assert.Equal(InboxIntakeOutcome.Ignored, await Receive(store, Note("Gone", null, Items.Noon) with { DeletedAt = Items.Noon }));
        Assert.Empty(store.Items);
    }

    // --- Acknowledgements ----------------------------------------------------

    [Fact]
    public void Routing_a_note_keeps_it_on_the_phone()
    {
        var item = PhoneNote();

        item.RouteToBacklog([Guid.CreateVersion7()], [], Arrival);

        Assert.False(item.ReplicaAckPending);
    }

    [Fact]
    public async Task Archiving_a_note_owes_the_phone_a_note_tombstone_stamped_at_the_archive()
    {
        var store = new InMemoryInboxStore();
        var item = PhoneNote();
        store.Seed(item);

        item.Archive(Arrival);

        var ack = Assert.Single(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
        Assert.Equal(InboxEnumMap.NoteKind, ack.Kind);
        Assert.Equal(Arrival, ack.AcknowledgedAt);
    }

    [Fact]
    public async Task Deleting_a_note_owes_the_phone_a_note_tombstone_whatever_state_it_is_in()
    {
        var store = new InMemoryInboxStore();
        var item = PhoneNote();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon.AddMinutes(6));
        store.Seed(item);

        await new DeleteItemCommandHandler(store, new FakeTimeProvider(Arrival)).Handle(new DeleteItemCommand(item.Id), Cancellation);

        var ack = Assert.Single(await new InboxCaptureOutbox(store).ListPendingAsync(Cancellation));
        Assert.Equal(item.Id, ack.CaptureId);
        Assert.Equal(InboxEnumMap.NoteKind, ack.Kind);
    }

    [Fact]
    public void A_note_made_on_this_desktop_still_owes_the_phone_its_tombstone()
    {
        var item = InboxItem.Capture("Kept here", new InboxSource(InboxEnumMap.ManualChannel, null), null, ContentKind.Note, Items.Noon, Items.Noon, replicaBacked: false);

        item.Archive(Arrival);

        Assert.True(item.ReplicaAckPending);
    }

    // --- Editing ---------------------------------------------------------------

    [Fact]
    public async Task Editing_a_note_changes_its_text_and_stamps_it_for_the_next_push()
    {
        var store = new InMemoryInboxStore();
        var item = PhoneNote();
        store.Seed(item);

        var result = await new EditNoteCommandHandler(store, new FakeTimeProvider(Arrival))
            .Handle(new EditNoteCommand(item.Id, "  Standup   notes ", " edited on the desktop "), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal("Standup notes", item.Title);
        Assert.Equal("edited on the desktop", item.BodyMd);
        Assert.Equal(Arrival, item.EditedAt);

        Assert.True(item.NotePushPending);
        var pending = await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation);
        var pushed = Assert.Single(pending.Value);
        Assert.Equal("edited on the desktop", pushed.BodyMd);
        Assert.Equal(Arrival, pushed.UpdatedAt);
        Assert.Null(pushed.DeletedAt);
    }

    [Fact]
    public async Task Only_a_note_is_edited_and_not_once_it_is_archived()
    {
        var store = new InMemoryInboxStore();
        var text = Items.FromPhone("A plain thought");
        var archived = PhoneNote();
        archived.Archive(Items.Noon.AddMinutes(6));
        store.Seed(text);
        store.Seed(archived);
        var handler = new EditNoteCommandHandler(store, new FakeTimeProvider(Arrival));

        var notNote = await handler.Handle(new EditNoteCommand(text.Id, "Changed", null), Cancellation);
        var gone = await handler.Handle(new EditNoteCommand(archived.Id, "Changed", null), Cancellation);
        var blank = await handler.Handle(new EditNoteCommand(archived.Id, " ", null), Cancellation);

        Assert.Equal(InboxErrors.NotANote, notNote.Error);
        Assert.Equal("inbox.item.invalid_transition", gone.Error.Code);
        Assert.Equal(InboxErrors.ItemNeedsTitle, blank.Error);
        Assert.Equal("A plain thought", text.Title);
    }

    [Fact]
    public async Task The_notes_to_push_are_the_live_notes_changed_here_oldest_first()
    {
        var store = new InMemoryInboxStore();
        var older = PhoneNote("Older");
        older.EditNote("Older", "a", Items.Noon.AddMinutes(20));
        var newer = PhoneNote("Newer");
        newer.EditNote("Newer", "b", Items.Noon.AddMinutes(30));
        var unchanged = PhoneNote("Unchanged");
        var archived = PhoneNote("Archived");
        archived.Archive(Items.Noon.AddMinutes(40));
        store.Seed(newer);
        store.Seed(older);
        store.Seed(unchanged);
        store.Seed(archived);
        store.Seed(Items.FromPhone("Not a note"));

        var pending = await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation);

        Assert.Equal(["Older", "Newer"], pending.Value.Select(note => note.Title));
    }

    [Fact]
    public async Task A_note_from_the_phone_is_never_pending_and_a_pushed_one_stops_waiting_unless_edited_since()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", "phone", Items.Noon);
        await Receive(store, note);
        await Receive(store, note with { BodyMd = "phone, later", UpdatedAt = Items.Noon.AddMinutes(1) });
        var item = store.Items[note.Id];

        Assert.False(item.NotePushPending);

        item.EditNote("Standup", "desk", Items.Noon.AddMinutes(20));
        var pending = (await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation)).Value;
        item.EditNote("Standup", "desk, again", Items.Noon.AddMinutes(21));

        await new MarkNotesPushedCommandHandler(store).Handle(new MarkNotesPushedCommand(pending), Cancellation);
        Assert.True(item.NotePushPending);

        var again = (await new ListPendingNotesQueryHandler(store).Handle(new ListPendingNotesQuery(), Cancellation)).Value;
        await new MarkNotesPushedCommandHandler(store).Handle(new MarkNotesPushedCommand(again), Cancellation);
        Assert.False(item.NotePushPending);
    }

    [Fact]
    public void A_desktop_change_is_stamped_after_the_copy_it_replaces_whatever_the_clocks_say()
    {
        // The phone's clock runs an hour ahead of this desktop's.
        var item = PhoneNote();
        item.ApplyNote("Standup", "from the phone", Items.Noon.AddHours(1));

        item.EditNote("Standup", "at the desk", Items.Noon.AddMinutes(10));
        Assert.True(item.EditedAt > Items.Noon.AddHours(1));

        var edited = item.EditedAt;
        item.Archive(Items.Noon.AddMinutes(11));
        Assert.True(item.EditedAt > edited);
    }

    [Fact]
    public async Task A_phone_edit_to_a_note_deleted_here_owes_the_tombstone_again_after_the_first_was_sent()
    {
        var store = new InMemoryInboxStore();
        var note = Note("Standup", null, Items.Noon);
        await Receive(store, note);
        await new DeleteItemCommandHandler(store, new FakeTimeProvider(Arrival)).Handle(new DeleteItemCommand(note.Id), Cancellation);

        // The tombstone goes out and is forgotten.
        var outbox = new InboxCaptureOutbox(store);
        await outbox.MarkSentAsync([.. (await outbox.ListPendingAsync(Cancellation)).Select(ack => ack.CaptureId)], Cancellation);
        Assert.Empty(await outbox.ListPendingAsync(Cancellation));

        // The phone, not yet told, edits it later than the deletion.
        var outcome = await Receive(store, note with { BodyMd = "edited offline", UpdatedAt = Arrival.AddMinutes(5) });

        Assert.Equal(InboxIntakeOutcome.AlreadyKnown, outcome);
        Assert.Empty(store.Items);
        var owed = Assert.Single(await outbox.ListPendingAsync(Cancellation));
        Assert.Equal(InboxEnumMap.NoteKind, owed.Kind);
        Assert.True(owed.AcknowledgedAt > Arrival.AddMinutes(5));
    }

    [Fact]
    public async Task A_capture_claiming_the_note_kind_stays_a_plain_thought()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxCaptureDto(Guid.CreateVersion7(), "Not really a note", "mobile", Items.Noon, Items.Noon, null, Kind: "note");

        await new InboxIntake(new Features.ReceiveCapture.ReceiveCaptureCommandHandler(store, new FakeTimeProvider(Arrival)))
            .ReceiveAsync(capture, Cancellation);

        Assert.Equal(ContentKind.Text, Assert.Single(store.Items.Values).Kind);
    }

    private static InboxItem PhoneNote(string title = "Standup")
    {
        var item = InboxItem.FromCapture(
            Guid.CreateVersion7(),
            title,
            new InboxSource("mobile", null),
            sourceUrl: null,
            ContentKind.Note,
            capturedAt: Items.Noon,
            receivedAt: Items.Noon.AddMinutes(5));
        item.StampEdited(Items.Noon);
        return item;
    }

    private static InboxNoteDto Note(string title, string? body, DateTimeOffset updatedAt, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), title, body, "mobile", Items.Noon, updatedAt);

    private static async Task<InboxIntakeOutcome> Receive(InMemoryInboxStore store, InboxNoteDto note)
    {
        var replication = new InboxNoteReplication(
            new ReceiveNoteCommandHandler(store, new FakeTimeProvider(Arrival)),
            new ListPendingNotesQueryHandler(store),
            new MarkNotesPushedCommandHandler(store));

        return await replication.ReceiveAsync(note, Cancellation);
    }
}
