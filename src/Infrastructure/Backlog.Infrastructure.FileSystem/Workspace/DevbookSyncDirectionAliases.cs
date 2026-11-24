using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The sync direction map and its imported mark, as the alias-keyed store they are
/// (local ADR 0060 §3): kept in <see cref="DevbookSubjectStateStore"/>'s files beside
/// the subject states, but carried by no feed, so — unlike the states — every device
/// moves them itself, from the registry's move groups. A store of its own, so the
/// follower treats it as <see cref="AliasKeyedStoreKind.Local"/> while the states stay
/// replicated.
/// </summary>
public sealed class DevbookSyncDirectionAliases(DevbookSubjectStateStore store) : IRepositoryAliasKeyedStore
{
    private readonly DevbookSubjectStateStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public AliasKeyedStoreKind Kind => AliasKeyedStoreKind.Local;

    public IReadOnlyCollection<string> HeldAliases() => _store.HeldDirectionAliases();

    public Task<bool> MoveAliasesAsync(IReadOnlyDictionary<string, string> oldToNew, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_store.MoveDirectionAliases(oldToNew));
    }
}
