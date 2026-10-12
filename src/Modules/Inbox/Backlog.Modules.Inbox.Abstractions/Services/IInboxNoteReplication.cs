using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The port the sync client moves notes through, both ways
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>).
/// <para>
/// A note is the one Inbox item that syncs both ways: the phone creates and
/// edits notes, and the desktop pushes its own edits back. Apart from
/// <see cref="IInboxIntake"/>, which takes a capture in once and then tells the
/// phone to forget it, because a note is never acknowledged away. The desktop's
/// archive or delete of a note leaves through <see cref="IInboxCaptureOutbox"/>
/// as the note's tombstone, like any other acknowledgement.
/// </para>
/// <para>
/// A head without an inbox store, the phone, leaves it null, as it does the intake.
/// </para>
/// </summary>
public interface IInboxNoteReplication
{
    /// <summary>
    /// Takes a pulled note document: creates the note, applies the later copy of
    /// one already here, or archives it for a tombstone. The later
    /// <see cref="InboxNoteDto.UpdatedAt"/> wins. Answers
    /// <see cref="InboxIntakeOutcome.Received"/> for a new note,
    /// <see cref="InboxIntakeOutcome.Updated"/> for a changed one,
    /// <see cref="InboxIntakeOutcome.Withdrawn"/> for one a tombstone archived,
    /// and <see cref="InboxIntakeOutcome.AlreadyKnown"/> or
    /// <see cref="InboxIntakeOutcome.Ignored"/> when nothing changed.
    /// </summary>
    Task<InboxIntakeOutcome> ReceiveAsync(InboxNoteDto note, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every live note this desktop changed and has not pushed yet, oldest change
    /// first: what the next push sends. A note taken in from the phone is not
    /// listed, and neither is an archived one, whose tombstone leaves through the
    /// capture outbox.
    /// </summary>
    Task<IReadOnlyList<InboxNoteDto>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>The replica answered the push of <paramref name="pushed"/>. Each
    /// stops waiting unless it changed again since. Called only after the push
    /// succeeded, so a failed one leaves them for the next.</summary>
    Task MarkPushedAsync(IReadOnlyList<InboxNoteDto> pushed, CancellationToken cancellationToken = default);
}
