using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.InferBatchOrder;

/// <summary>The "Before you route" panel's "Ask the AI to order": the order the
/// plan drafter reads into a batch, as dependencies the person can turn on or
/// off. <paramref name="PlanTag"/> is the tag the proposal minted, sigil
/// included; <paramref name="Repositories"/> are the panel's per-item choices,
/// as the route takes them.</summary>
public sealed record InferBatchOrderQuery(
    IReadOnlyList<Guid> Ids,
    string PlanTag,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Repositories = null);

/// <summary>
/// Asks the drafter to order the batch and hands its answer to the backlog
/// target to read — the answer is entry text, and only the adapter may know
/// that grammar (local ADR 0002). What comes back is tier-three proposals:
/// <see cref="DependencyTier.Inferred"/>, between two items of the batch, each
/// one a person's to keep or turn off before anything is routed. Nothing is
/// imported here; the batch still goes through its own route.
/// <para>
/// Opt-in by construction: nothing asks this on its own, because every ask is
/// a model call. The drafter is optional, as it is for Create plan, and its
/// absence answers <c>inbox.plan.not_configured</c> with the drafter's reason.
/// </para>
/// <para>
/// The repositories the answer may name are the batch's own — every item's,
/// as the panel has them now — and an answer naming another is refused whole
/// by the adapter, the rule a drafted plan is held to.
/// </para>
/// </summary>
public sealed class InferBatchOrderQueryHandler(
    IInboxItemRepository items,
    IInboxBacklogTarget target,
    IInboxPlanDrafter? drafter = null)
    : IQueryHandler<InferBatchOrderQuery, Result<IReadOnlyList<ProposedDependency>>>
{
    /// <summary>What an inferred dependency says about itself in the panel,
    /// where a stated one quotes the item's text.</summary>
    public const string Reason = "The AI read the batch and put this one after it.";

    public async Task<Result<IReadOnlyList<ProposedDependency>>> Handle(
        InferBatchOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Ids);

        if (drafter is null || !drafter.IsAvailable)
            return InboxErrors.PlanNotConfigured(drafter?.UnavailableReason);

        var (_, routable) = await RouteBatchToBacklogCommandHandler.SortAsync(items, query.Ids, cancellationToken).ConfigureAwait(false);
        var batch = routable.OrderBy(entry => entry.Position).Select(entry => entry.Item).ToList();

        // One item has nothing to be ordered against.
        if (batch.Count < 2) return Result.Success<IReadOnlyList<ProposedDependency>>([]);

        var choices = new InboxBatchRouteChoicesDto(Repositories: query.Repositories);
        var repositories = batch.ToDictionary(item => item.Id, item => RouteBatchToBacklogCommandHandler.RepositoriesFor(item, choices));
        IReadOnlyList<string> allowed = [.. repositories.Values.SelectMany(repos => repos).Distinct(StringComparer.OrdinalIgnoreCase)];
        var planTag = query.PlanTag.Trim().TrimStart('+');

        var request = new InboxPlanDraftRequestDto(
            Guid.Empty,
            planTag,
            string.Empty,
            null,
            "batch",
            [],
            allowed,
            planTag,
            [.. batch.Select(item => new InboxPlanDraftBatchItemDto(
                item.Id,
                item.Title,
                item.BodyMd,
                item.SourceUrl,
                item.KindSlug,
                repositories[item.Id]))]);

        var draft = await drafter.DraftAsync(request, cancellationToken).ConfigureAwait(false);
        if (draft.IsFailure) return draft.Error;

        var edges = target.ReadDraftedOrder(draft.Value.PlanMarkdown, [.. batch.Select(item => item.Id)], allowed);
        if (edges.IsFailure) return edges.Error;

        var titles = batch.ToDictionary(item => item.Id, item => item.Title);

        return Result.Success<IReadOnlyList<ProposedDependency>>([.. edges.Value.Select(edge => new ProposedDependency(
            edge.From,
            DependencyTarget.ForItem(edge.To, titles[edge.To]),
            Reason,
            DependencyTier.Inferred))]);
    }
}
