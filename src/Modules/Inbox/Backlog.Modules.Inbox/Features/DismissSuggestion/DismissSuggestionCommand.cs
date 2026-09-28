using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.DismissSuggestion;

/// <summary>The reader turned a suggestion down. <paramref name="Key"/> is the
/// suggestion's own key, as <c>SuggestQuery</c> answered it.</summary>
public sealed record DismissSuggestionCommand(Guid Id, string Key);

/// <summary>Records the refusal on the item, so Classification never offers that
/// suggestion for it again. Idempotent: a refusal already recorded writes
/// nothing.</summary>
public sealed class DismissSuggestionCommandHandler(IInboxItemRepository items)
    : ICommandHandler<DismissSuggestionCommand, Result>
{
    public async Task<Result> Handle(DismissSuggestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Key)) return Result.Failure(InboxErrors.SuggestionKeyRequired);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        if (item.DismissSuggestion(command.Key))
        {
            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
