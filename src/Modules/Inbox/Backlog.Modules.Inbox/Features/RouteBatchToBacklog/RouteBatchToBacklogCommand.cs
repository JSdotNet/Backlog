using System.Data.Common;
using System.Text.RegularExpressions;

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
/// a selection. <paramref name="Choices"/> are what the person decided in the
/// panel first — the tag the proposal minted, a repository per item, the
/// dependencies left on — and a batch routed without them is routed as it
/// always was.</summary>
public sealed record RouteBatchToBacklogCommand(
    IReadOnlyList<Guid> Ids,
    Guid? ListId = null,
    InboxBatchRouteChoicesDto? Choices = null);

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
/// items go is the caller's list of ids. A tag handed back from the proposal is
/// that same fresh tag, minted a moment earlier so the panel could show it.
/// </para>
/// <para>
/// The confirmed dependencies go to the target as facts — which other items of
/// the batch, which tasks — and never as tokens: which of an item's entries an
/// <c>after:</c> names is the grammar's business, and the adapter writes it.
/// Two things are decided here first. A dependency on an item that is not going
/// (refused above) is dropped, since there is nothing for it to name. And a set
/// that loops is refused whole, before Tasks is asked — the panel will not
/// confirm one, and the command does not take its word for it. The items go in
/// the order their dependencies give (<see cref="InboxBatchOrder"/>), so the
/// document reads first-things-first.
/// </para>
/// <para>
/// A merge the person turned on (<see cref="InboxBatchRouteChoicesDto.Merges"/>)
/// keeps one item and sends only it: the items it was kept in favour of are
/// taken out of the document and archived as duplicates of it once it is routed
/// and saved. What they waited on, and what waited on them, is carried onto the
/// kept item (<see cref="InboxBatchOrder.Carry"/>) — the same thought still
/// waits on the same things — and checked for a loop with the rest. This is done
/// here rather than
/// in the pane, so the two halves of one decision are one command. When the kept
/// item is not routed, its duplicates are left in the Inbox with it and named
/// (<c>inbox.batch.merge_not_routed</c>). A merge naming an item that is not
/// going, or an item twice, is read for what it can mean: each item is kept or
/// merged at most once, and anything else is ignored.
/// </para>
/// </summary>
public sealed partial class RouteBatchToBacklogCommandHandler(
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

        var minted = await MintPlanTagAsync(organizer, command.ListId, cancellationToken).ConfigureAwait(false);
        if (minted.IsFailure) return minted.Error;

        var planTag = minted.Value;
        if (command.Choices?.PlanTag is { } chosen)
        {
            if (!PlanTagShape().IsMatch(chosen.Trim())) return InboxErrors.BatchPlanTagInvalid(chosen.Trim());
            planTag = chosen.Trim();
        }

        var (failed, routable) = await SortAsync(items, command.Ids, cancellationToken).ConfigureAwait(false);

        if (routable.Count == 0) return new InboxBatchRoutedDto(planTag, [], Ordered(failed));

        // The items a merge folds into another are not sent; they wait for the
        // one kept, and are archived as its duplicates once it has gone.
        var keptFor = Merges(command.Choices, routable);
        var extras = routable.Where(entry => keptFor.ContainsKey(entry.Item.Id)).ToList();
        routable = [.. routable.Where(entry => !keptFor.ContainsKey(entry.Item.Id))];

        var going = routable.Select(entry => entry.Item.Id).ToList();
        var dependencies = InboxBatchOrder.Carry(command.Choices?.Dependencies ?? [], keptFor)
            .Where(dependency => going.Contains(dependency.From))
            .ToList();
        var edges = InboxBatchOrder.Edges(dependencies)
            .Where(edge => edge.From != edge.To && going.Contains(edge.To))
            .ToList();

        // A task's value goes into a metadata token as it is, so one that would
        // not stay one token is refused before anything is sent.
        if (dependencies.FirstOrDefault(dependency => dependency.To.Kind == DependencyTargetKind.Task && !IsTokenValue(dependency.To.After)) is { } invalid)
        {
            return InboxErrors.BatchDependencyInvalid(invalid.To.Title);
        }

        var loops = InboxBatchOrder.Loops(going, edges);
        if (loops.Count > 0)
        {
            var titles = routable.ToDictionary(entry => entry.Item.Id, entry => entry.Item.Title);
            return InboxErrors.BatchDependencyLoop([.. loops.Select(loop => InboxBatchOrder.Name(loop, id => titles[id]))]);
        }

        var repositories = routable.ToDictionary(entry => entry.Item.Id, entry => RepositoriesFor(entry.Item, command.Choices));
        var byId = routable.ToDictionary(entry => entry.Item.Id, entry => entry.Item);

        var request = new InboxBatchRouteRequestDto(
            [.. InboxBatchOrder.Order(going, edges).Select(id => RouteToBacklogCommandHandler.RequestFor(byId[id], attachmentFiles) with
            {
                RepoIds = repositories[id],
                AfterItems = [.. edges.Where(edge => edge.From == id).Select(edge => edge.To).Distinct()],
                AfterTasks = [.. dependencies
                    .Where(dependency => dependency.From == id && dependency.To.Kind == DependencyTargetKind.Task)
                    .Select(dependency => dependency.To.After!)
                    .Distinct(StringComparer.Ordinal)],
            })],
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
            failed.AddRange(extras.Select(entry => (entry.Position, new InboxBatchFailureDto(entry.Item.Id, InboxErrors.BatchMergeNotRouted))));
            return new InboxBatchRoutedDto(planTag, [], Ordered(failed));
        }

        var byItem = answer.Routed.ToDictionary(routed => routed.InboxItemId);
        var now = clock.GetUtcNow();
        var routedItems = new List<InboxRoutedDto>(answer.Routed.Count);

        foreach (var (at, item) in routable)
        {
            if (!byItem.TryGetValue(item.Id, out var routed)) continue;

            // The repositories it went to, which are the panel's choice when the
            // person made one — the routing records where the entries are.
            item.RouteToBacklog(routed.TaskIds, repositories[item.Id], now);

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

        var archived = await ArchiveMergedAsync(extras, keptFor, routedItems, now, failed, cancellationToken).ConfigureAwait(false);

        return new InboxBatchRoutedDto(planTag, routedItems, Ordered(failed)) { Archived = archived };
    }

    /// <summary>
    /// The merges that can be done, as "merged item → the item kept". A kept item
    /// must be going and not itself merged; a merged item must be going, not the
    /// kept one, not kept by another merge, and merged once. Kept items are read
    /// in the batch's order, so which merge claims an item twice named is the
    /// same on every run.
    /// </summary>
    private static Dictionary<Guid, Guid> Merges(InboxBatchRouteChoicesDto? choices, List<(int Position, InboxItem Item)> routable)
    {
        var keptFor = new Dictionary<Guid, Guid>();
        if (choices?.Merges is not { Count: > 0 } merges) return keptFor;

        var going = routable.Select(entry => entry.Item.Id).ToHashSet();
        var kept = merges.Keys.Where(going.Contains).ToHashSet();

        foreach (var (_, item) in routable)
        {
            if (!kept.Contains(item.Id) || !merges.TryGetValue(item.Id, out var duplicates) || duplicates is null) continue;

            foreach (var duplicate in duplicates)
            {
                if (duplicate == item.Id || !going.Contains(duplicate) || kept.Contains(duplicate)) continue;
                keptFor.TryAdd(duplicate, item.Id);
            }
        }

        return keptFor;
    }

    /// <summary>Archives each merged item as a duplicate of the one kept, once
    /// that one is routed and saved; otherwise names it as left behind. Each is
    /// its own save, as each routed item is, and answers the ones archived in
    /// the order asked.</summary>
    private async Task<IReadOnlyList<Guid>> ArchiveMergedAsync(
        List<(int Position, InboxItem Item)> extras,
        Dictionary<Guid, Guid> keptFor,
        List<InboxRoutedDto> routedItems,
        DateTimeOffset now,
        List<(int Position, InboxBatchFailureDto Failure)> failed,
        CancellationToken cancellationToken)
    {
        var routed = routedItems.Select(routed => routed.InboxItemId).ToHashSet();
        var archived = new List<Guid>();

        foreach (var (at, item) in extras.OrderBy(entry => entry.Position))
        {
            var kept = keptFor[item.Id];
            if (!routed.Contains(kept))
            {
                failed.Add((at, new InboxBatchFailureDto(item.Id, InboxErrors.BatchMergeNotRouted)));
                continue;
            }

            try
            {
                item.Archive(now, kept);
            }
            catch (InvalidInboxTransitionException refused)
            {
                failed.Add((at, new InboxBatchFailureDto(item.Id, InboxErrors.InvalidTransition(refused.Message))));
                continue;
            }

            try
            {
                await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
                archived.Add(item.Id);
            }
            catch (Exception failure) when (IsStoreFailure(failure))
            {
                // Nothing was made for it, so there is nothing to lose: it stays
                // in the Inbox beside the item that went, and says why.
                failed.Add((at, new InboxBatchFailureDto(item.Id, InboxErrors.BatchMergeSaveFailed(failure.Message))));
            }
        }

        return archived;
    }

    /// <summary>A new plan tag for a batch, sigil included: named after the
    /// list when there is one, else <see cref="SelectionTagStem"/>. Fails with
    /// <c>inbox.list.not_found</c> for a list that is gone. Shared with the
    /// proposal, which mints the tag the panel shows.</summary>
    internal static async Task<Result<string>> MintPlanTagAsync(
        IInboxOrganizerRepository organizer,
        Guid? listId,
        CancellationToken cancellationToken)
    {
        var stem = SelectionTagStem;
        if (listId is { } id)
        {
            var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);
            if (lists.FirstOrDefault(list => list.Id == id) is not { } list) return Result.Failure<string>(InboxErrors.ListNotFound);
            stem = list.Name;
        }

        return "+" + InboxPlanTag.For(stem, Guid.CreateVersion7());
    }

    /// <summary>The asked ids sorted into the ones that cannot be routed — gone,
    /// already routed, archived — and the ones that can, each with its place in
    /// the order asked. A repeated id is asked once. Shared with the proposal,
    /// so the panel refuses an item for exactly the reasons the route will.</summary>
    internal static async Task<(List<(int Position, InboxBatchFailureDto Failure)> Failed, List<(int Position, InboxItem Item)> Routable)> SortAsync(
        IInboxItemRepository items,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        var failed = new List<(int Position, InboxBatchFailureDto Failure)>();
        var routable = new List<(int Position, InboxItem Item)>();
        var position = 0;

        foreach (var id in ids.Distinct())
        {
            var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);

            if (item is null) failed.Add((position, new InboxBatchFailureDto(id, InboxErrors.ItemNotFound)));
            else if (RouteToBacklogCommandHandler.Refusal(item) is { } refused) failed.Add((position, new InboxBatchFailureDto(id, refused)));
            else routable.Add((position, item));

            position++;
        }

        return (failed, routable);
    }

    /// <summary>The repositories an item goes to: the person's choice when the
    /// panel made one, else the ones it is assigned — trimmed, blank ones
    /// dropped, each once.</summary>
    private static IReadOnlyList<string> RepositoriesFor(InboxItem item, InboxBatchRouteChoicesDto? choices)
    {
        var chosen = choices?.Repositories is { } overrides && overrides.TryGetValue(item.Id, out var picked) && picked is not null
            ? picked
            : item.RepoIds;

        return [.. chosen
            .Where(repo => !string.IsNullOrWhiteSpace(repo))
            .Select(repo => repo.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether a value can be written as one <c>after:</c> token: not
    /// empty, and with no whitespace or backtick to end the token early.</summary>
    private static bool IsTokenValue(string? value) =>
        !string.IsNullOrEmpty(value) && !value.Any(character => char.IsWhiteSpace(character) || character == '`');

    /// <summary>What a store's save throws when the write itself failed — the
    /// database, the disk, a connection in the wrong state. Cancellation is not
    /// one of them and is never caught here.</summary>
    private static bool IsStoreFailure(Exception failure) =>
        failure is DbException or IOException or UnauthorizedAccessException
        || (failure is InvalidOperationException && failure is not ObjectDisposedException);

    private static List<InboxBatchFailureDto> Ordered(List<(int Position, InboxBatchFailureDto Failure)> failed) =>
        [.. failed.OrderBy(entry => entry.Position).Select(entry => entry.Failure)];

    /// <summary>A plan tag as the metadata line reads one: the sigil, then a
    /// word that opens with a letter (<c>[A-Za-z][\w-]*</c>).</summary>
    [GeneratedRegex(@"^\+[A-Za-z][\w-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlanTagShape();
}
