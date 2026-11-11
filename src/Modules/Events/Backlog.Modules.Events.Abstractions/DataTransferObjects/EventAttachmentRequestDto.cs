namespace Backlog.Modules.Events.Abstractions.DataTransferObjects;

/// <summary>
/// An attachment request as it arrives from the phone
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>): attach the Inbox note
/// <paramref name="NoteId"/> to the knowledge event <paramref name="EventId"/> as a Talk note.
/// Plain data; the sync client reads it off the task feed, and nothing here carries a
/// serialization attribute (guideline ADR 0014).
/// </summary>
/// <param name="RequestId">The request's own id: the document it travelled as.</param>
/// <param name="EventId">The knowledge event, or <see cref="Guid.Empty"/> when the document named none.</param>
/// <param name="NoteId">The Inbox note, or <see cref="Guid.Empty"/> when the document named none.</param>
/// <param name="NoteTitle">The note's title as the phone showed it, kept on the link.</param>
public sealed record EventAttachmentRequestDto(Guid RequestId, Guid EventId, Guid NoteId, string? NoteTitle);
