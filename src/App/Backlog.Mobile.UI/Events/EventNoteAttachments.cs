using System.Text.Json;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Events;

/// <summary>
/// The phone's one write about a knowledge event: attaching a Talk note to it
/// (<c>.devbook/domain/events/features.md#attach-a-talk-note</c>). It queues an
/// <c>event-attachment-request</c> through the <see cref="DeviceOutbox"/>, so a request made
/// with no signal is kept; a desktop then adds the Talk Note Link, and the next pull of the
/// event brings it back. The Inbox note itself is not changed and holds nothing of the event
/// (local ADR 0050 §3).
/// <para>
/// One per device, a singleton, over the outbox singleton.
/// </para>
/// </summary>
public sealed class EventNoteAttachments(DeviceOutbox outbox, TimeProvider clock)
{
    /// <summary>Raised when a request is queued, leaves the outbox, or parks there — the
    /// outbox's own signal. May be raised off the render thread.</summary>
    public event Action? Changed
    {
        add => outbox.Changed += value;
        remove => outbox.Changed -= value;
    }

    /// <summary>
    /// Queues the request to attach the note <paramref name="noteId"/>, titled
    /// <paramref name="noteTitle"/> as the person sees it, to the event <paramref name="eventId"/>,
    /// and answers the request's id. A request for the same note and event still waiting on the
    /// phone is answered instead of queuing a second, and is tried again at once when the network
    /// had parked it. One the service refused does not count: it will be refused again, so asking
    /// once more queues a new request.
    /// </summary>
    public async Task<Guid> AttachAsync(Guid eventId, Guid noteId, string? noteTitle, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("An attachment names the event.", nameof(eventId));
        if (noteId == Guid.Empty) throw new ArgumentException("An attachment names the note.", nameof(noteId));

        if (Waiting().LastOrDefault(waiting => !waiting.Entry.Refused
            && waiting.Request.EventId == eventId && waiting.Request.NoteId == noteId) is { } same)
        {
            if (same.Entry.IsParked) await outbox.RetryAsync(same.Entry.Id, cancellationToken);
            return same.Request.RequestId;
        }

        var title = string.IsNullOrWhiteSpace(noteTitle) ? null : noteTitle.Trim();
        var request = new EventAttachmentRequestDto(Guid.CreateVersion7(), eventId, noteId, title);
        var change = EventAttachmentReplicaDocuments.ToChange(request, clock.GetUtcNow());

        await outbox.EnqueueAsync(EventAttachmentRequestOutboxKind.Token, request.RequestId, TaskOutboxKind.Write(change), cancellationToken);
        return request.RequestId;
    }

    /// <summary>The event the note was last attached to on this phone while that request still
    /// waits in the outbox, parked or not; null when none waits.</summary>
    public Guid? PendingEventFor(Guid noteId) => PendingFor(noteId)?.EventId;

    /// <summary>The note's latest attachment request still on the phone, with the outbox entry
    /// carrying it — whose <see cref="OutboxEntry.IsParked"/>, <see cref="OutboxEntry.Refused"/>
    /// and <see cref="OutboxEntry.LastError"/> say why it has not left; null when none waits.</summary>
    public PendingEventAttachment? PendingFor(Guid noteId) =>
        Waiting().LastOrDefault(waiting => waiting.Request.NoteId == noteId) is { } latest
            ? new PendingEventAttachment(latest.Request.EventId, latest.Entry)
            : null;

    /// <summary>Every attachment request still on the phone, oldest first, with its entry. An
    /// entry this build cannot read is passed over; the outbox still sends or parks it on its own.</summary>
    private IEnumerable<Queued> Waiting()
    {
        foreach (var entry in outbox.Entries)
        {
            if (entry.Kind != EventAttachmentRequestOutboxKind.Token) continue;

            EventAttachmentRequestDto? request;

            try
            {
                request = EventAttachmentReplicaDocuments.ToRequest(TaskOutboxKind.Read(entry));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                request = null;
            }

            if (request is not null) yield return new Queued(request, entry);
        }
    }

    /// <summary>An attachment request read back from the outbox, with the entry carrying it.</summary>
    private sealed record Queued(EventAttachmentRequestDto Request, OutboxEntry Entry);
}
