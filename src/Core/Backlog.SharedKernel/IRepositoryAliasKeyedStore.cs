namespace Backlog.SharedKernel;

/// <summary>
/// A store that keeps data under a repository alias, re-keyed when the registry
/// relabels a repository (local ADR 0060 §3).
/// <para>
/// In the shared kernel because the stores that answer it sit in modules and adapters
/// that may not see each other, and the follower that drives them sits in the GitHub
/// adapter, which owns the registry.
/// </para>
/// </summary>
public interface IRepositoryAliasKeyedStore
{
    /// <summary>Whether the store travels between devices, which decides who re-keys
    /// it.</summary>
    AliasKeyedStoreKind Kind { get; }

    /// <summary>The normalized aliases the store holds data under now.</summary>
    IReadOnlyCollection<string> HeldAliases();

    /// <summary>
    /// Moves the data under each old alias to its new one, the whole mapping at once,
    /// so a swap never runs one move into the other. Where a new alias already holds
    /// data, that data wins. An alias the mapping does not name stays where it is.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    Task<bool> MoveAliasesAsync(IReadOnlyDictionary<string, string> oldToNew, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="MoveAliasesAsync(IReadOnlyDictionary{string, string}, CancellationToken)"/>,
    /// saying why the data moves. A replicated store whose copies carry stamps stamps
    /// an <see cref="AliasMoveOrigin.Edit"/> move now, and keeps each copy's own stamp
    /// on a <see cref="AliasMoveOrigin.Backstop"/> move. A store with nothing to stamp
    /// moves the same way for both, which is what this default does.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    Task<bool> MoveAliasesAsync(
        IReadOnlyDictionary<string, string> oldToNew,
        AliasMoveOrigin origin,
        CancellationToken cancellationToken) =>
        MoveAliasesAsync(oldToNew, cancellationToken);
}
