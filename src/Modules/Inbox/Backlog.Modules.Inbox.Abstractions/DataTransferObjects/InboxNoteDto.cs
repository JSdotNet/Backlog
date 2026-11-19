namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// A note as it crosses the replica (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>):
/// an Inbox Item of <c>Content Kind</c> <c>note</c>, reduced to what the phone
/// and the desktop share about it.
/// <para>
/// <paramref name="Id"/> is the note's id on every device. <paramref name="UpdatedAt"/>
/// is the note's own last-write-wins stamp, when its text or files last changed;
/// the later one wins on both sides. <paramref name="DeletedAt"/> is set on a
/// tombstone, which the desktop pushes when it archives or deletes a note.
/// </para>
/// <para>
/// <paramref name="Channel"/> is the capture source the note was made through,
/// <c>mobile</c> for one made on the phone. <paramref name="Attachments"/> name
/// files already in the attachment store, metadata only, as a capture's do
/// (<c>.devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md</c>).
/// </para>
/// <para>
/// <paramref name="Kind"/> is the item's content kind slug, <c>note</c> or
/// <c>meeting</c> (<c>.devbook/domain/inbox/domain.md#meeting</c>). It decides the
/// kind of an item the receiving side creates and never changes one it holds.
/// </para>
/// </summary>
public sealed record InboxNoteDto(
    Guid Id,
    string Title,
    string? BodyMd,
    string Channel,
    DateTimeOffset CapturedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<InboxCaptureAttachmentDto>? Attachments = null,
    string Kind = InboxEnumMap.NoteKind);
