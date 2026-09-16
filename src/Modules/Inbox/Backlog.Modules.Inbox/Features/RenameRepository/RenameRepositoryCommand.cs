using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.RenameRepository;

/// <summary>
/// Re-points every item assigned to a repository by its old <c>owner/name</c> at
/// the new one, after the repository was renamed on GitHub and the registry
/// followed. Registry ids, as <c>AssignRepositories</c> takes them; nothing here
/// resolves a name.
/// <para>
/// Only the assignment moves. An item's routing record is where it <em>went</em>,
/// written once and never edited — the domain says so — and a repository being
/// called something else now does not change where the item went then.
/// </para>
/// <para>
/// Idempotent: after one run no item names the old id, so a second run is a pure
/// read. Items of every status are covered, archived included, for the same
/// reason the tag picker reads them all — an archived item is still one a
/// reader can open, and its chip should read the current name.
/// </para>
/// </summary>
public sealed record RenameRepositoryCommand(string OldId, string NewId)
{
    public static readonly Error BlankId = Error.Validation(
        "inbox.repository.rename_blank_id",
        "Both the old and the new repository id are needed.");
}

/// <summary>Runs the rewrite and answers how many items it changed, so the
/// settings screen that asked has a number for its status line.</summary>
public sealed class RenameRepositoryCommandHandler(IInboxItemRepository items)
    : ICommandHandler<RenameRepositoryCommand, Result<int>>
{
    public async Task<Result<int>> Handle(RenameRepositoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.OldId) || string.IsNullOrWhiteSpace(command.NewId))
        {
            return RenameRepositoryCommand.BlankId;
        }

        if (string.Equals(command.OldId.Trim(), command.NewId.Trim(), StringComparison.OrdinalIgnoreCase)) return 0;

        // Snapshotted before the loop saves into the store it read from.
        var stored = (await items.ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var changed = 0;

        foreach (var item in stored)
        {
            if (!item.RenameRepository(command.OldId.Trim(), command.NewId.Trim())) continue;

            await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }
}
