using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// One note as the phone last heard of it: the replica's document, the stamps it
/// is ordered by, and nothing the phone worked out for itself — the
/// <c>note_view</c> row of <c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>.
/// </summary>
/// <param name="UpdatedAt">The note's own last-write-wins stamp.</param>
/// <param name="DeletedAt">Set on a tombstone: the desktop archived or deleted
/// the note. The row is kept, hidden, so an older copy arriving in a later page
/// cannot bring it back.</param>
/// <param name="ServerTimestamp">The replica's ordering stamp, which breaks a tie
/// between two copies with the same <paramref name="UpdatedAt"/>. Zero on a note
/// this phone wrote itself and has not pulled back yet.</param>
/// <param name="Note">The document, kept whole, so a field this build does not
/// know is carried rather than dropped when the phone edits it.</param>
public sealed record NoteViewRow(
    Guid Id,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    long ServerTimestamp,
    TaskPayload Note)
{
    public string Title => Note.Title;

    /// <summary>The Markdown body; empty for a note with none.</summary>
    public string Body => Note.ContentMd;

    public IReadOnlyList<AttachmentMetadata> Attachments => Note.Attachments ?? [];

    /// <summary>Gone from the phone's list: the desktop archived or deleted it.</summary>
    public bool IsHidden => DeletedAt is not null;
}
