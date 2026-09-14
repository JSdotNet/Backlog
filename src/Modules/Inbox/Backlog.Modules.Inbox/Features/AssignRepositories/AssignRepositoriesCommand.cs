using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.AssignRepositories;

/// <summary>Replaces the repositories the item will route to. Registry ids as
/// the settings store hands them out; nothing here resolves a name.</summary>
public sealed record AssignRepositoriesCommand(Guid Id, IReadOnlyList<string> RepoIds);

public sealed class AssignRepositoriesCommandHandler(IInboxItemRepository items)
    : ICommandHandler<AssignRepositoriesCommand, Result>
{
    public async Task<Result> Handle(AssignRepositoriesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);

        item.SetRepoIds(command.RepoIds);

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
