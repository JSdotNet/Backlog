using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.ListPendingNotes;

/// <summary>Every live note this desktop changed and has not pushed yet: what the
/// sync client pushes next (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>).</summary>
public sealed record ListPendingNotesQuery;

/// <summary>
/// Reads the notes to push: those carrying <see cref="InboxItem.NotePushPending"/>,
/// oldest change first. A note taken in from the phone is not among them, so the
/// desktop never echoes the phone's own copy back. An archived note is not either:
/// its tombstone leaves through the capture outbox, and a live copy sent beside it
/// would bring it back.
/// </summary>
public sealed class ListPendingNotesQueryHandler(IInboxItemRepository items)
    : IQueryHandler<ListPendingNotesQuery, Result<IReadOnlyList<InboxNoteDto>>>
{
    public async Task<Result<IReadOnlyList<InboxNoteDto>>> Handle(
        ListPendingNotesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await items.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<InboxNoteDto> pending =
        [
            .. all
                .Where(item => item.IsNote && item.NotePushPending && item.Status is not InboxStatus.Archived)
                .OrderBy(item => item.EditedAt)
                .Select(ToNote),
        ];

        return Result.Success(pending);
    }

    /// <summary>The note as it crosses the replica: its text, its files as
    /// metadata, its own stamp and nothing the desktop decided about it.</summary>
    internal static InboxNoteDto ToNote(InboxItem item) => new(
        item.Id,
        item.Title,
        string.IsNullOrEmpty(item.BodyMd) ? null : item.BodyMd,
        item.Source.Channel,
        item.CapturedAt,
        item.EditedAt,
        DeletedAt: null,
        Tags: [.. item.Tags.Select(tag => tag.Name)],
        Attachments: item.Attachments.Count == 0
            ? null
            : [.. item.Attachments.Select(file => new InboxCaptureAttachmentDto(
                file.Id, file.Name, file.ContentType, file.SizeBytes, file.Sha256))]);
}

/// <summary>The replica took these notes, each as it was at its
/// <see cref="InboxNoteDto.UpdatedAt"/>.</summary>
public sealed record MarkNotesPushedCommand(IReadOnlyList<InboxNoteDto> Pushed);

/// <summary>
/// Clears the push flag on each note the replica took, unless the note changed
/// again while the push was in flight: that later edit still goes out next time.
/// A note gone since (deleted) is skipped; its tombstone is the acknowledgement's.
/// </summary>
public sealed class MarkNotesPushedCommandHandler(IInboxItemRepository items)
    : ICommandHandler<MarkNotesPushedCommand, Result>
{
    public async Task<Result> Handle(MarkNotesPushedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var note in command.Pushed)
        {
            if (await items.GetAsync(note.Id, cancellationToken).ConfigureAwait(false) is not { NotePushPending: true } item)
            {
                continue;
            }

            item.MarkNotePushed(note.UpdatedAt);
            if (!item.NotePushPending) await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
