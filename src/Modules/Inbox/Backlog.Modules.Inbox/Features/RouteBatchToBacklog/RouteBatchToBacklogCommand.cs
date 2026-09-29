using System.Data.Common;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RouteBatchToBacklog;

/// <summary>Routes several items to the backlog as one batch: one plan import
/// whose entries share a new plan tag, named after <paramref name="ListId"/>'s
/// list when the batch is a list's open items and <c>inbox-batch</c> when it is
/// a selection.</summary>
public sealed record RouteBatchToBacklogCommand(IReadOnlyList<Guid> Ids, Guid? ListId = null);

/// <summary>
/// <c>RouteToBacklog</c> once for several items, with the one difference that
/// makes it a batch rather than a loop: Tasks is asked once, for one document,
/// and takes all of it or none. So the order of writes is the single route's —
/// Tasks first, the items only once it has answered — and a refusal leaves every
/// item exactly as it was.
/// <para>
/// What would make the import refuse for one item's sake is refused here first,
/// per item and with the single route's own errors: an item already routed or
/// archived, or gone. Those are named and the rest still go, as the other acts
/// across a selection name theirs. What only the target can judge — whether an
/// item's text stays one entry in a document, whether its repositories are
/// ones the workspace knows — it judges per item too, and the items it leaves
/// out come back named with their own reasons. Only Tasks' refusal of the
/// document itself is every sent item's.
/// </para>
/// <para>
/// Every batch is a new plan. The tag's eight hex digits come from a fresh batch
/// id, so routing a list twice — once today, once after more has arrived — makes
/// two plans, and the second import's clear-then-write (ADR 0007) cannot reach
/// the first one's entries. The list is only where the name comes from; which
/// items go is the caller's list of ids.
/// </para>
/// </summary>
public sealed class RouteBatchToBacklogCommandHandler(
    IInboxItemRepository items,
    IInboxOrganizerRepository organizer,
    IInboxBacklogTarget target,
    TimeProvider clock,
    IInboxAttachmentFiles? attachmentFiles = null)
    : ICommandHandler<RouteBatchToBacklogCommand, Result<InboxBatchRoutedDto>>
{
    /// <summary>What a selection's plan tag is named: it has no name of its own.</summary>
    internal const string SelectionTagStem = "inbox-batch";

    public async Task<Result<InboxBatchRoutedDto>> Handle(
        RouteBatchToBacklogCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Ids);

        var stem = SelectionTagStem;
        if (command.ListId is { } listId)
        {
            var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
            if (lists.FirstOrDefault(list => list.Id == listId) is not { } list) return InboxErrors.ListNotFound;
            stem = list.Name;
        }

        var planTag = "+" + InboxPlanTag.For(stem, Guid.CreateVersion7());

        var failed = new List<(int Position, InboxBatchFailureDto Failure)>();
        var routable = new List<(int Position, InboxItem Item)>();
        var position = 0;

        foreach (var id in command.Ids.Distinct())
        {
            var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);

            if (item is null) failed.Add((position, new InboxBatchFailureDto(id, InboxErrors.ItemNotFound)));
            else if (RouteToBacklogCommandHandler.Refusal(item) is { } refused) failed.Add((position, new InboxBatchFailureDto(id, refused)));
            else routable.Add((position, item));

            position++;
        }

        if (routable.Count == 0) return new InboxBatchRoutedDto(planTag, [], Ordered(failed));

        var request = new InboxBatchRouteRequestDto(
            [.. routable.Select(entry => RouteToBacklogCommandHandler.RequestFor(entry.Item, attachmentFiles))],
            planTag);

        var answer = await target.CreateBatchTasksAsync(request, cancellationToken).ConfigureAwait(false);

        // The items the target left out come back with their own reasons —
        // notes it could not keep apart, a repository the workspace does not
        // know, an entry Tasks did not answer for — and pass through as given.
        var positions = routable.ToDictionary(entry => entry.Item.Id, entry => entry.Position);
        failed.AddRange(answer.Refused.Select(failure => (positions[failure.Id], failure)));

        if (answer.DocumentRefused is { } documentRefused)
        {
            // Tasks' own refusal of the document, before it wrote anything: every
            // item that was in it is refused with it, and only those. Wrapped
            // here and nowhere else, so the person reads first that nothing moved.
            var refusal = InboxErrors.BatchRefused(documentRefused);
            var leftOut = answer.Refused.Select(failure => failure.Id).ToHashSet();
            failed.AddRange(routable
                .Where(entry => !leftOut.Contains(entry.Item.Id))
                .Select(entry => (entry.Position, new InboxBatchFailureDto(entry.Item.Id, refusal))));
            return new InboxBatchRoutedDto(planTag, [], Ordered(failed));
        }

        var byItem = answer.Routed.ToDictionary(routed => routed.InboxItemId);
        var now = clock.GetUtcNow();
        var routedItems = new List<InboxRoutedDto>(answer.Routed.Count);

        foreach (var (at, item) in routable)
        {
            if (!byItem.TryGetValue(item.Id, out var routed)) continue;

            item.RouteToBacklog(routed.TaskIds, [.. item.RepoIds], now);

            try
            {
                await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
                routedItems.Add(routed);
            }
            catch (Exception failure) when (IsStoreFailure(failure))
            {
                // The entries exist and this item does not know it. Named with
                // them, as a partial single route names what it made first; the
                // items saved before it stay routed and the ones after it are
                // still tried, each being its own save.
                failed.Add((at, new InboxBatchFailureDto(item.Id, InboxErrors.BatchSaveFailed(routed.TaskIds, failure.Message))));
            }
        }

        return new InboxBatchRoutedDto(planTag, routedItems, Ordered(failed));
    }

    /// <summary>What a store's save throws when the write itself failed — the
    /// database, the disk, a connection in the wrong state. Cancellation is not
    /// one of them and is never caught here.</summary>
    private static bool IsStoreFailure(Exception failure) =>
        failure is DbException or IOException or UnauthorizedAccessException
        || (failure is InvalidOperationException && failure is not ObjectDisposedException);

    private static List<InboxBatchFailureDto> Ordered(List<(int Position, InboxBatchFailureDto Failure)> failed) =>
        [.. failed.OrderBy(entry => entry.Position).Select(entry => entry.Failure)];
}
