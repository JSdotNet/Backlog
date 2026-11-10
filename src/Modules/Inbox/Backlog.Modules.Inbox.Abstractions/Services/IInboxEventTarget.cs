using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// The Events context, as the Inbox is allowed to see it: a place that takes a
/// routed item as a Spotted knowledge event and answers its id — the
/// <c>ItemTriaged</c> events route of <c>.devbook/domain/inbox/domain.md#itemtriaged</c>,
/// realised as a call, as <see cref="IInboxBrainTarget"/> realises the route to the
/// Brain.
/// <para>
/// A port declared here and answered in <c>src/Infrastructure</c>, never a reference
/// to Events. A host may leave it out, and then To Event is not offered.
/// </para>
/// </summary>
public interface IInboxEventTarget
{
    /// <summary>Whether To Event is offered right now: while the Events area is on
    /// (<c>.devbook/domain/events/context.md#events-area</c>). Asked on render, so
    /// turning the flag on shows the route at once.</summary>
    bool IsOffered { get; }

    /// <summary>Makes a Spotted knowledge event from the item, in the series of its
    /// watched page or a new one, and answers the event's id. A refusal or failure
    /// on Events' side answers <see cref="InboxErrors.EventNotMadeCode"/>.</summary>
    Task<Result<Guid>> SpotEventAsync(InboxEventRequestDto request, CancellationToken cancellationToken = default);
}
