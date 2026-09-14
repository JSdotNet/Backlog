using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RouteToBacklog;

/// <summary>Turns an item into backlog entries — one per assigned repository, or
/// one untargeted entry — and records where they went. <c>ItemTriaged</c> with
/// the route set to Tasks, realised as a call on
/// <see cref="IInboxBacklogTarget"/> rather than an event.</summary>
public sealed record RouteToBacklogCommand(Guid Id);

/// <summary>
/// The order of the two writes is the decision here. Tasks is asked first and
/// the item is only marked routed once Tasks has answered with ids, so a
/// failure on the far side leaves the item exactly as it was — unrouted, still
/// in the queue, and free to be tried again. The reverse order would leave an
/// item claiming entries that were never made. The cost is the opposite gap: a
/// crash between the two writes leaves entries with a source id and an item that
/// does not know about them, which the person can see in the backlog and which
/// routing again would only duplicate. The aggregate refuses a second routing,
/// so that case surfaces as an error rather than a second set of entries.
/// </summary>
public sealed class RouteToBacklogCommandHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    TimeProvider clock)
    : ICommandHandler<RouteToBacklogCommand, Result<InboxRoutedDto>>
{
    public async Task<Result<InboxRoutedDto>> Handle(
        RouteToBacklogCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return InboxErrors.ItemNotFound;

        // Refused before Tasks is asked, so an item that cannot take the
        // routing costs no entries. The same guard the aggregate applies below;
        // stated here because "route twice" is the one mistake a double-click
        // makes and it should make no entry.
        if (item.IsRouted) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed again").Message);
        if (item.Status is InboxStatus.Archived) return InboxErrors.InvalidTransition(
            new InvalidInboxTransitionException(item.Status, "routed").Message);

        var request = new InboxRouteRequestDto(
            item.Id,
            item.Title,
            item.BodyMd,
            item.SourceUrl,
            [.. item.Tags.Select(tag => tag.Name)],
            [.. item.RepoIds]);

        var created = await target.CreateTasksAsync(request, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure) return created.Error;

        item.RouteToBacklog(created.Value, [.. item.RepoIds], clock.GetUtcNow());

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return new InboxRoutedDto(item.Id, created.Value);
    }
}
