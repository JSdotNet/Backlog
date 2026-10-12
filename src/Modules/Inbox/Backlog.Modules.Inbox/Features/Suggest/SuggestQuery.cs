using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.Suggest;

/// <summary>What Classification proposes for one item — tags, repositories, a
/// destination — less whatever the reader has already turned down for it.
/// A read: nothing is applied until the reader accepts a suggestion through the
/// act it names.</summary>
public sealed record SuggestQuery(Guid Id);

/// <summary>The suggestions of several items at once — what the list's
/// "suggested" markers read. One pass over the backlog's tags and the rules
/// for the lot, rather than one per row.</summary>
public sealed record SuggestManyQuery(IReadOnlyList<Guid> Ids);

/// <summary>
/// Reads the item, the backlog's tags and the reader's routing rules, and hands
/// them to <see cref="InboxClassifier"/>. Both ports are optional, like the plan
/// drafter: a host that registers neither still gets the suggestions the item's
/// own text supports.
/// </summary>
public sealed class SuggestQueryHandler(
    IInboxItemRepository items,
    IBacklogTagSource? backlogTags = null,
    IInboxRoutingRules? routingRules = null)
    : IQueryHandler<SuggestQuery, Result<IReadOnlyList<InboxSuggestionDto>>>,
      IQueryHandler<SuggestManyQuery, Result<IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>>>>
{
    public async Task<Result<IReadOnlyList<InboxSuggestionDto>>> Handle(SuggestQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await items.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure<IReadOnlyList<InboxSuggestionDto>>(InboxErrors.ItemNotFound);

        // A decided item is offered nothing, so there is nothing to read for it.
        if (!item.IsOpen || item.IsRouted) return Result.Success<IReadOnlyList<InboxSuggestionDto>>([]);

        var tags = backlogTags is null
            ? []
            : await backlogTags.TagsInUseAsync(cancellationToken).ConfigureAwait(false);
        var rules = routingRules?.Current ?? [];

        return Result.Success(InboxClassifier.Suggest(item, tags, rules));
    }

    /// <summary>Each id that names an item, with what it would be offered —
    /// nothing for a decided one. An id with no item is left out rather than
    /// failing the rest: a row can vanish between the list's read and this one.
    /// The tags and the rules are read once for the whole batch.</summary>
    public async Task<Result<IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>>>> Handle(SuggestManyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var answers = new Dictionary<Guid, IReadOnlyList<InboxSuggestionDto>>();
        IReadOnlyList<string>? tags = null;
        var rules = routingRules?.Current ?? [];

        foreach (var id in query.Ids.Distinct())
        {
            var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (item is null) continue;

            if (!item.IsOpen || item.IsRouted)
            {
                answers[id] = [];
                continue;
            }

            tags ??= backlogTags is null
                ? []
                : await backlogTags.TagsInUseAsync(cancellationToken).ConfigureAwait(false);
            answers[id] = InboxClassifier.Suggest(item, tags, rules);
        }

        return Result.Success<IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>>>(answers);
    }
}
