using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RouteToEvent;

/// <summary>Makes an item a Spotted knowledge event and records that it went
/// there — To Event (<c>.devbook/domain/inbox/features.md#route-to-event</c>),
/// <c>ItemTriaged</c> with the route set to Events, realised as a call on
/// <see cref="IInboxEventTarget"/> rather than an event.</summary>
public sealed record RouteToEventCommand(Guid Id);

/// <summary>
/// The order of To Brain, for its reason: Events is asked first and the item is
/// marked routed only once the event exists, so a failure on Events' side leaves
/// the item open and unrouted. The aggregate's refusals are checked before Events
/// is asked, so a second routing makes no second event.
/// <para>
/// The target is optional: a host composed without Events answers
/// <see cref="InboxErrors.EventsUnavailableCode"/>, as it does while the Events
/// area is off.
/// </para>
/// </summary>
public sealed class RouteToEventCommandHandler(
    IInboxItemRepository items,
    TimeProvider clock,
    IInboxEventTarget? target = null)
    : ICommandHandler<RouteToEventCommand, Result<InboxEventRoutedDto>>
{
    public async Task<Result<InboxEventRoutedDto>> Handle(
        RouteToEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (target is not { IsOffered: true }) return InboxErrors.EventsUnavailable;

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;

        if (RouteToBacklogCommandHandler.Refusal(item) is { } refused) return refused;

        var spotted = await target
            .SpotEventAsync(new InboxEventRequestDto(item.Id, item.Title, item.SourceUrl, FactsOf(item.EventFacts)), cancellationToken)
            .ConfigureAwait(false);
        if (spotted.IsFailure) return spotted.Error;

        item.RouteToEvent(spotted.Value, clock.GetUtcNow());
        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxEventRoutedDto(item.Id, spotted.Value);
    }

    private static InboxEventFactsDto? FactsOf(InboxEventFacts? facts) => facts is null
        ? null
        : new InboxEventFactsDto(DateOf(facts.Start), DateOf(facts.End), facts.Place, facts.PageUrl, facts.PageTitle);

    private static InboxEventDateDto? DateOf(InboxEventDate? date) =>
        date is null ? null : new InboxEventDateDto(date.Date, date.Time, date.Offset);
}
