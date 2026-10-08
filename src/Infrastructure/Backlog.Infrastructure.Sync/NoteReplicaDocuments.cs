using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a note is written on the task feed (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>):
/// a task-shaped document under the note's own id with the kind token
/// <c>note</c>, following the pattern local ADR 0009 set for <c>capture</c>.
/// The wire half only; what a note is, and which copy wins on this desktop, is
/// the Inbox's, behind <see cref="Backlog.Modules.Inbox.Abstractions.Services.IInboxNoteReplication"/>.
/// <para>
/// The phone's <c>NoteFold</c> reads the same token as a literal, for the reason
/// <see cref="TaskReplicaMerge"/> gives for <c>capture</c>: neither side may see
/// the other. The Inbox's <see cref="InboxEnumMap.NoteKind"/> is the same word,
/// because a note's kind and its document's kind are one fact.
/// </para>
/// </summary>
public static class NoteReplicaDocuments
{
    /// <summary>The kind token a note document carries.</summary>
    public const string NoteType = InboxEnumMap.NoteKind;

    /// <summary>Whether a document is a note's. Ordinal, as every token on the
    /// feed is compared.</summary>
    public static bool IsNote(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, NoteType, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note as a task-shaped change: its id and own stamp, the title, the body
    /// as <c>ContentMd</c>, the channel it was made through in <c>SourceInboxId</c>
    /// as a capture's is, its tags, and its files as metadata. The Tasks defaults
    /// for status and priority, so no field but the type is unusual.
    /// </summary>
    public static TaskChange ToChange(InboxNoteDto note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new TaskChange(note.Id, note.UpdatedAt, note.DeletedAt, new TaskPayload(
            note.Title,
            note.BodyMd ?? string.Empty,
            NoteType,
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: note.CapturedAt,
            SourceInboxId: note.Channel,
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: null,
            View: null,
            Effort: null,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: note.Tags ?? [],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: [],
            Attachments: note.Attachments is { Count: > 0 } files
                ? [.. files.Select(file => new AttachmentMetadata(file.Id, file.Name, file.ContentType, file.SizeBytes, file.Sha256))]
                : null));
    }

    /// <summary>The note a pulled document carries, as the Inbox takes it. A
    /// channel the document does not name is filed as unknown rather than
    /// dropped, as a capture's is.</summary>
    public static InboxNoteDto ToNote(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new InboxNoteDto(
            change.Id,
            change.Task.Title,
            string.IsNullOrWhiteSpace(change.Task.ContentMd) ? null : change.Task.ContentMd,
            change.Task.SourceInboxId ?? "unknown",
            change.Task.CreatedAt,
            change.UpdatedAt,
            change.DeletedAt,
            change.Task.Tags,
            change.Task.Attachments is { Count: > 0 } files
                ? [.. files.Select(file => new InboxCaptureAttachmentDto(file.Id, file.Name, file.ContentType, file.SizeBytes, file.Sha256))]
                : null);
    }
}
