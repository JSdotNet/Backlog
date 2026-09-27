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
    : IQueryHandler<SuggestQuery, Result<IReadOnlyList<InboxSuggestionDto>>>
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
}
