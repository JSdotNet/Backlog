using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Events.Abstractions.Services;

/// <summary>What <see cref="IEventAttachmentRequests.ApplyAsync"/> did with one request.</summary>
public enum EventAttachmentOutcome
{
    /// <summary>The event gained the Talk Note Link and was saved.</summary>
    Attached,

    /// <summary>The event already links the note. Nothing was written: a second desktop, or a
    /// second tap, applying the same request does no harm.</summary>
    AlreadyAttached,

    /// <summary>The request does not name both an event and a note
    /// (<c>attachment_request.incomplete</c>). Nothing was written, and asking again will not
    /// change that.</summary>
    Incomplete,

    /// <summary>This device holds no event by that id — not yet, or the Events area keeps none
    /// here. Nothing was written.</summary>
    UnknownEvent,
}

/// <summary>
/// Where the sync client hands an <c>event-attachment-request</c> document the phone wrote: one
/// Talk note to attach to one knowledge event (<c>.devbook/domain/events/features.md#attach-a-talk-note</c>,
/// local ADR 0050 §3). The note is named by its Inbox id and keeps the title the phone showed;
/// the Inbox holds nothing of the event.
/// </summary>
public interface IEventAttachmentRequests
{
    /// <summary>Applies <paramref name="request"/>. Answers every request, never throws for one
    /// it cannot apply.</summary>
    Task<EventAttachmentOutcome> ApplyAsync(EventAttachmentRequestDto request, CancellationToken cancellationToken = default);
}
