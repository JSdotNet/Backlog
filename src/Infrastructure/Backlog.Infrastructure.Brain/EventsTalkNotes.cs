using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers Events' <see cref="IEventTalkNotes"/> over the Inbox's <see cref="IInboxItems"/> and
/// the Brain's <see cref="IBrainNoteWriter"/> (local ADR 0073 §8): on harvest, a Talk note is
/// written into the Brain's inbox area through the one writer Capture and To Brain use, and the
/// Inbox note is archived. Neither side learns of the event.
/// <para>
/// The note keeps the Inbox note's title and body, word for word; the writer adds the heading
/// and <c>meta</c> block every Brain note carries, with source <c>inbox</c>. Only an open note —
/// Unprocessed or Deferred — moves: a note already routed has its home, and an archived one was
/// dismissed. A note with files attached — a slide photo, a recording — stays in the Inbox, where
/// its files are, because the Brain note would carry its words and lose the rest. A host composed
/// without the Inbox moves nothing. The archive raises
/// <see cref="IInboxChangeSignal"/>, so an open Inbox pane reloads.
/// </para>
/// </summary>
public sealed class EventsTalkNotes(IInboxItems? inbox, IBrainNoteWriter writer, IInboxChangeSignal? changes = null) : IEventTalkNotes
{
    public async Task<EventBrainNoteDto?> FileIntoBrainAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (inbox is null) return null;

        var snapshot = await inbox.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot.Items.FirstOrDefault(item => item.Id == noteId) is not { } note
            || note.Status is not (InboxStatus.Unprocessed or InboxStatus.Deferred)
            || note.Attachments.Count > 0)
        {
            return null;
        }

        // The body as To Brain writes it: the note's words, and its source link when it has one.
        var body = InboxBrainTarget.BodyOf(new InboxBrainNoteRequestDto(note.Id, note.Title, note.BodyMd, note.SourceUrl));
        var written = await writer
            .WriteAsync(new BrainNoteDraft(note.Title, body, BrainNoteSource.Inbox), cancellationToken)
            .ConfigureAwait(false);

        return written.Written
            ? new EventBrainNoteDto(written.RelativePath!, BrainNoteText.TitleOf(note.Title) ?? note.Title)
            : null;
    }

    public async Task<Result> ArchiveAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        if (inbox is null) return Result.Failure(NoInbox);

        var archived = await inbox.ArchiveAsync(noteId, cancellationToken).ConfigureAwait(false);
        if (archived is not Error) changes?.Raise();
        return archived;
    }

    private static readonly Error NoInbox =
        Error.Unexpected("event_talk_note.no_inbox", "There is no Inbox here to archive the note in.");
}
