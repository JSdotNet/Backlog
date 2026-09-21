using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.RenameRepository;

/// <summary>
/// Re-points every entry that names a repository by its old <c>owner/name</c> at
/// the new one, after the repository was renamed on GitHub and the registry
/// followed.
/// <para>
/// The registry can carry its own row across a rename; it cannot carry the
/// entries, which live here and file themselves against the id rather than the
/// alias. Left alone, they would still name the old coordinate — and the next
/// <c>ReconcileRepositoryIds</c> pass, seeing an id-shaped value the registry no
/// longer knows, would register it as a directory-less repository: a ghost of
/// the old name beside the new one, with every entry still pointing at the ghost.
/// This command is what makes that pass find nothing to do.
/// </para>
/// <para>
/// Idempotent, like the reconcile pass and for the same reason: after one run no
/// entry names the old id, so a second run is a pure read. Soft-deleted entries
/// are left alone, exactly as the reconcile pass leaves them — a tombstone is
/// not read back into a chip, so a stale id on one registers nothing.
/// </para>
/// </summary>
public sealed record RenameRepositoryCommand(string OldId, string NewId)
{
    public static readonly Error BlankId = Error.Validation(
        "repository.rename_blank_id",
        "Both the old and the new repository id are needed.");
}

/// <summary>Runs the rewrite and answers how many entries it changed, for the
/// same reason the reconcile pass does: the caller is a settings screen with a
/// status line, and the count is what it has to say.</summary>
public sealed class RenameRepositoryCommandHandler(ITaskRepository entries)
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

        // Snapshotted before the loop saves into the store it read from, as the
        // reconcile pass does.
        var stored = (await entries.ListAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var changed = 0;

        foreach (var entry in stored)
        {
            if (!entry.RenameRepository(command.OldId.Trim(), command.NewId.Trim())) continue;

            await entries.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
            changed++;
        }

        return changed;
    }
}
