using System.Text.Json;

using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How an attachment request the phone writes travels on the task feed: one document per
/// request, under the request's own id, with the kind token <c>event-attachment-request</c> and
/// <c>{requestId, eventId, noteId, noteTitle}</c> as its text
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>). A command, not a note or an
/// event: a desktop applies it through Events and then retires it with a tombstone, on the terms
/// <see cref="ImpedimentEditReplicaDocuments"/> gives an impediment edit (local ADR 0035 §8).
/// <para>
/// The one place its text is written and read (guideline ADR 0014): the phone writes the
/// document with <see cref="ToChange"/> and the desktop reads it with <see cref="ToRequest"/>.
/// The kind token is a literal the phone's folds duplicate to skip it; persisted on every
/// replica, so never renamed.
/// </para>
/// </summary>
public static class EventAttachmentReplicaDocuments
{
    /// <summary>The kind token an attachment request carries.</summary>
    public const string RequestType = "event-attachment-request";

    /// <summary>What the activity log calls a request whose note had no title.</summary>
    public const string UntitledTitle = "Talk note";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Whether a document is an attachment request. Ordinal, as every token on the feed is compared.</summary>
    public static bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, RequestType, StringComparison.Ordinal);
    }

    /// <summary>The request as a task-shaped change under its own id, stamped
    /// <paramref name="askedAt"/> and titled with the note's title for the sync log.</summary>
    public static TaskChange ToChange(EventAttachmentRequestDto request, DateTimeOffset askedAt)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = JsonSerializer.Serialize(
            new RequestText(request.RequestId, request.EventId, request.NoteId, request.NoteTitle),
            Json);

        return WholeDocumentChange.Of(
            request.RequestId,
            string.IsNullOrWhiteSpace(request.NoteTitle) ? UntitledTitle : request.NoteTitle,
            RequestType,
            text,
            askedAt);
    }

    /// <summary>
    /// The request a document carries, or null when its text does not read as one or names
    /// another request than the document's id. An event or note it leaves out reads as
    /// <see cref="Guid.Empty"/>, for Events to refuse as incomplete.
    /// </summary>
    public static EventAttachmentRequestDto? ToRequest(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (string.IsNullOrWhiteSpace(change.Task.ContentMd)) return null;

        RequestText? text;

        try
        {
            text = JsonSerializer.Deserialize<RequestText>(change.Task.ContentMd, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (text is null || text.RequestId != change.Id) return null;

        return new EventAttachmentRequestDto(text.RequestId, text.EventId, text.NoteId, text.NoteTitle);
    }

    /// <summary>The tombstone that retires <paramref name="change"/>, on
    /// <see cref="ImpedimentEditReplicaDocuments.Retire"/>'s terms: deleted at
    /// <paramref name="now"/>, or one tick after its own stamp on a desktop whose clock is
    /// behind the phone's.</summary>
    public static TaskChange Retire(TaskChange change, DateTimeOffset now) =>
        ImpedimentEditReplicaDocuments.Retire(change, now);

    /// <summary>The document's text, field for field.</summary>
    private sealed record RequestText(Guid RequestId, Guid EventId, Guid NoteId, string? NoteTitle);
}
