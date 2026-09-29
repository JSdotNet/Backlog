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

        var going = routable.Select(entry => entry.Item.Id).ToList();
        var dependencies = (command.Choices?.Dependencies ?? [])
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

        return new InboxBatchRoutedDto(planTag, routedItems, Ordered(failed));
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
    internal static IReadOnlyList<string> RepositoriesFor(InboxItem item, InboxBatchRouteChoicesDto? choices)
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
