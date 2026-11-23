using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.DomainModels;

/// <summary>
/// What the phone sends to attach a Talk note to a knowledge event
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>): the event and the Inbox note, by
/// id, and the note's title as the phone showed it. A request, not content: it carries no file.
/// <para>
/// Its rules are <c>.devbook/domain/events/domain.invariants.md#attachment-request</c>. It names
/// exactly one event and one note (<c>attachment_request.incomplete</c>), and it never changes:
/// there is no mutator, so a desktop applies it whole and the sync client then retires it.
/// </para>
/// </summary>
public sealed class AttachmentRequest
{
    private AttachmentRequest(Guid id, Guid eventId, Guid noteId, string noteTitle)
    {
        Id = id;
        EventId = eventId;
        NoteId = noteId;
        NoteTitle = noteTitle;
    }

    /// <summary>The request's own id: the document it travelled as.</summary>
    public Guid Id { get; }

    /// <summary>The knowledge event the note is attached to.</summary>
    public Guid EventId { get; }

    /// <summary>The Inbox note's id.</summary>
    public Guid NoteId { get; }

    /// <summary>The note's title as it read on the phone, trimmed; empty when none was sent.</summary>
    public string NoteTitle { get; }

    /// <summary>A request for <paramref name="noteId"/> on <paramref name="eventId"/>. Refused
    /// unless it names both (<c>attachment_request.incomplete</c>).</summary>
    public static Result<AttachmentRequest> Create(Guid id, Guid eventId, Guid noteId, string? noteTitle)
    {
        if (eventId == Guid.Empty || noteId == Guid.Empty) return EventsErrors.AttachmentRequestIncomplete();

        return new AttachmentRequest(id, eventId, noteId, noteTitle?.Trim() ?? string.Empty);
    }

    /// <summary>The link the event gains: a Talk note named by its Inbox id, with the title the
    /// phone showed.</summary>
    public NoteLink ToNoteLink() => NoteLink.Create(NoteLinkKind.Talk, NoteId.ToString("D"), NoteTitle);
}
