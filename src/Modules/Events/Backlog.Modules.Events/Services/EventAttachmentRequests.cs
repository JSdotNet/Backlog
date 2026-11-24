using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// Answers <see cref="IEventAttachmentRequests"/> over the event repository: builds the
/// <see cref="AttachmentRequest"/>, loads its event, and links the note as a Talk note
/// (<c>.devbook/domain/events/domain.md#attachment-request</c>). The event's own
/// <see cref="KnowledgeEvent.LinkNote"/> keeps a note once whatever its title, so a request met
/// twice saves nothing the second time and the first title stands.
/// </summary>
internal sealed class EventAttachmentRequests(IKnowledgeEventRepository events, KnowledgeEventSaved? saved = null)
    : IEventAttachmentRequests
{
    public async Task<EventAttachmentOutcome> ApplyAsync(EventAttachmentRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = AttachmentRequest.Create(request.RequestId, request.EventId, request.NoteId, request.NoteTitle);
        if (created is not Ok<AttachmentRequest>(var attachment)) return EventAttachmentOutcome.Incomplete;

        if (await events.GetAsync(attachment.EventId, cancellationToken).ConfigureAwait(false) is not { } knowledgeEvent)
        {
            // Absent and removed read alike through GetAsync; only an absent event may still arrive.
            return await events.IsRemovedAsync(attachment.EventId, cancellationToken).ConfigureAwait(false)
                ? EventAttachmentOutcome.RemovedEvent
                : EventAttachmentOutcome.UnknownEvent;
        }

        var link = attachment.ToNoteLink();
        if (knowledgeEvent.Notes.Contains(link)) return EventAttachmentOutcome.AlreadyAttached;

        knowledgeEvent.LinkNote(link);
        await events.SaveAsync(knowledgeEvent, cancellationToken).ConfigureAwait(false);

        // The desktop's answer to the phone travels on the knowledge event feed, so it is
        // announced as a person's change would be.
        saved?.Raise();

        return EventAttachmentOutcome.Attached;
    }
}
