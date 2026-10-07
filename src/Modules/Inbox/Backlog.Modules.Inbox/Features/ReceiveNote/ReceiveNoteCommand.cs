using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ReceiveCapture;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Inbox.Features.ReceiveNote;

/// <summary>A note document pulled from the replica, to apply here
/// (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>).</summary>
public sealed record ReceiveNoteCommand(InboxNoteDto Note);

/// <summary>
/// The desktop half of a note's two-way sync. A new id becomes an Inbox Item of
/// kind <c>note</c>; the later copy of a note already here replaces its title,
/// body and files; a tombstone archives it. The later
/// <see cref="InboxNoteDto.UpdatedAt"/> wins, compared with the note's own
/// <see cref="InboxItem.EditedAt"/>.
/// <para>
/// Unlike <see cref="ReceiveCaptureCommandHandler"/>, nothing here owes the
/// phone an acknowledgement: a note is never acknowledged away
/// (<c>.devbook/domain/inbox/domain.md#note</c>). The phone never sends a
/// tombstone either, since it never archives an item; a tombstone arriving here
/// is another desktop's.
/// </para>
/// <para>
/// The handler never fails: every document has an outcome, and the sync client
/// counts them. A note with no title is ignored rather than thrown on, for the
/// reason the capture intake gives: a throw would stall the pull on that page.
/// </para>
/// </summary>
public sealed class ReceiveNoteCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxAttachmentSource? attachmentSource = null,
    IInboxAttachmentFiles? attachmentFiles = null,
    ILogger<ReceiveNoteCommandHandler>? logger = null)
    : ICommandHandler<ReceiveNoteCommand, Result<InboxIntakeOutcome>>
{
    private readonly ILogger _logger = logger ?? NullLogger<ReceiveNoteCommandHandler>.Instance;

    public async Task<Result<InboxIntakeOutcome>> Handle(
        ReceiveNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var note = command.Note;
        var withdrawn = note.DeletedAt is not null;
        var existing = await items.GetAsync(note.Id, cancellationToken).ConfigureAwait(false);

        if (existing is null) return await CreateAsync(note, withdrawn, cancellationToken).ConfigureAwait(false);

        // An id this desktop holds as something else: a note is a note because
        // it was created as one, and a document never turns an item into one.
        if (!existing.IsNote) return InboxIntakeOutcome.AlreadyKnown;

        if (withdrawn) return await WithdrawAsync(existing, note, cancellationToken).ConfigureAwait(false);

        if (existing.Status is InboxStatus.Archived)
        {
            // The phone edited a note before it heard of the archive. The archive
            // stands, and its tombstone is owed again under a later stamp, or the
            // phone would keep the copy it edited.
            if (note.UpdatedAt > existing.EditedAt)
            {
                existing.RestateArchive(clock.GetUtcNow());
                await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);
            }

            return InboxIntakeOutcome.AlreadyKnown;
        }

        var changed = !string.IsNullOrWhiteSpace(note.Title) && existing.ApplyNote(note.Title, note.BodyMd, note.UpdatedAt);
        var added = existing.RecordAttachments(ReceiveCaptureCommandHandler.AttachmentsOf(note.Attachments));

        // The text is saved before any file is fetched, so a fetch that takes a
        // while cannot carry an older copy of the note over an edit made here
        // meanwhile.
        if (changed || added > 0) await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        await FetchAndKeepAsync(existing, cancellationToken).ConfigureAwait(false);

        return changed ? InboxIntakeOutcome.Updated : InboxIntakeOutcome.AlreadyKnown;
    }

    private async Task<InboxIntakeOutcome> CreateAsync(InboxNoteDto note, bool withdrawn, CancellationToken cancellationToken)
    {
        // Deleted here: not a new note. A live copy means the phone edited it
        // before it heard of the deletion, so the tombstone is owed again, under a
        // stamp later than that edit, or the replica would keep the phone's copy
        // and the phone would keep showing it.
        var deleted = await items.GetDeletedCaptureAsync(note.Id, cancellationToken).ConfigureAwait(false);
        if (deleted is not null || await items.WasDismissedAsync(note.Id, cancellationToken).ConfigureAwait(false))
        {
            if (withdrawn) return InboxIntakeOutcome.Ignored;

            if (deleted is null || note.UpdatedAt >= deleted.DeletedAt)
            {
                var now = clock.GetUtcNow();
                await items.RememberDeletedCaptureAsync(
                    new InboxDeletedCapture(
                        note.Id,
                        string.IsNullOrWhiteSpace(note.Title) ? deleted?.Title ?? "Note" : note.Title,
                        note.CapturedAt,
                        now > note.UpdatedAt ? now : note.UpdatedAt.AddTicks(1),
                        InboxEnumMap.NoteKind),
                    cancellationToken).ConfigureAwait(false);
            }

            return InboxIntakeOutcome.AlreadyKnown;
        }

        if (withdrawn || string.IsNullOrWhiteSpace(note.Title)) return InboxIntakeOutcome.Ignored;

        var item = InboxItem.FromCapture(
            note.Id,
            note.Title,
            new InboxSource(InboxEnumMap.NormalizeChannel(note.Channel), Person: null),
            sourceUrl: null,
            ContentKind.Note,
            note.CapturedAt,
            clock.GetUtcNow(),
            string.IsNullOrWhiteSpace(note.BodyMd) ? null : note.BodyMd.Trim(),
            replicaBacked: true);

        item.RecordAttachments(ReceiveCaptureCommandHandler.AttachmentsOf(note.Attachments));

        if (note.Tags is { Count: > 0 } tags)
        {
            item.SetTags(tags
                .Select(tag => (tag ?? string.Empty).Trim().TrimStart('#').Trim())
                .Where(tag => tag.Length > 0 && !tag.StartsWith('@'))
                .Select(tag => new InboxTag(tag, AutoGenerated: false)));
        }

        // Last, after every mutator: the note keeps the phone's stamp, so the copy
        // this desktop pushes back is not newer than the one the replica holds.
        item.StampEdited(note.UpdatedAt);

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        await FetchAndKeepAsync(item, cancellationToken).ConfigureAwait(false);

        return InboxIntakeOutcome.Received;
    }

    private async Task<InboxIntakeOutcome> WithdrawAsync(InboxItem existing, InboxNoteDto note, CancellationToken cancellationToken)
    {
        // Open is what a tombstone can still close; a routed note cannot be
        // archived, and an archived one already is. An edit made here after the
        // tombstone was written wins, as any later copy does.
        if (!existing.IsOpen || note.DeletedAt is not { } deletedAt || deletedAt <= existing.EditedAt)
        {
            return InboxIntakeOutcome.AlreadyKnown;
        }

        existing.Archive(clock.GetUtcNow());

        // The replica is the source of this tombstone, so there is nothing to
        // tell it: clearing the flag keeps this desktop from echoing it back.
        existing.MarkReplicaAcknowledged();

        await items.SaveAsync(existing, cancellationToken).ConfigureAwait(false);

        return InboxIntakeOutcome.Withdrawn;
    }

    /// <summary>
    /// Fetches the note's waiting files and keeps what became of them. The note is
    /// read again before the save and only its files are carried over, because the
    /// reader may have edited it on this desktop while the files came down, and a
    /// save of the copy read before would put the old text back.
    /// </summary>
    private async Task FetchAndKeepAsync(InboxItem item, CancellationToken cancellationToken)
    {
        if (!await ReceiveCaptureCommandHandler
                .FetchWaitingAsync(item, attachmentSource, attachmentFiles, clock, _logger, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var current = await items.GetAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (current is null) return;

        if (!ReferenceEquals(current, item)) current.LoadAttachments(item.Attachments);

        await items.SaveAsync(current, cancellationToken).ConfigureAwait(false);
    }
}
