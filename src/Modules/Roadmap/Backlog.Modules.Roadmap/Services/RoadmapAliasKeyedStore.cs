using Backlog.Modules.Roadmap.Features.MoveRepositoryAliases;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The plan as a store keyed by repository alias, for the follower that re-keys such
/// stores when the registry relabels a repository (local ADR 0060 §3).
/// <para>
/// <see cref="AliasKeyedStoreKind.Replicated"/>: the plan travels as one document (local
/// ADR 0018), last write wins on its stamp. The device that made the relabel moves it as
/// an <see cref="AliasMoveOrigin.Edit"/>, a new version stamped now that reaches the
/// others as a newer plan. Every other device that finds its copy still under an old
/// alias moves it as a <see cref="AliasMoveOrigin.Backstop"/>, at the stamp the copy
/// already had: the same re-key of the same version, so it never outranks the editing
/// device's plan, and a write of its own not pushed yet still travels, re-keyed.
/// </para>
/// <para>
/// A move that changed the plan raises the plan's change notice either way, as every
/// other write through <see cref="RoadmapPlanning"/> does: the roadmap redraws on it, and
/// the sync loop pushes on it. A push at a kept stamp is an echo or refused as older.
/// </para>
/// <para>
/// A singleton over singletons: the plan's repository, its gate and its change notice all
/// live as long as the host.
/// </para>
/// </summary>
internal sealed class RoadmapAliasKeyedStore(
    IRoadmapPlanRepository plans,
    RoadmapPlanGate gate,
    RoadmapPlanChanges changes) : IRepositoryAliasKeyedStore
{
    private readonly MoveRepositoryAliasesCommandHandler _move = new(plans, gate);

    public AliasKeyedStoreKind Kind => AliasKeyedStoreKind.Replicated;

    /// <summary>
    /// The aliases the stored plan colours or files under.
    /// <para>
    /// Blocks the calling thread on the plan's load, because the port asks synchronously
    /// and the plan is only read asynchronously. The load runs on the thread pool, so a
    /// caller on a UI thread cannot deadlock on its own context, but it does wait for the
    /// database while it blocks.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<string> HeldAliases() =>
        Task.Run(() => plans.LoadAsync()).GetAwaiter().GetResult().RepositoryAliases();

    /// <summary>An <see cref="AliasMoveOrigin.Edit"/> move: the port's two-argument form
    /// is the relabelling device's.</summary>
    public Task<bool> MoveAliasesAsync(
        IReadOnlyDictionary<string, string> oldToNew,
        CancellationToken cancellationToken) =>
        MoveAliasesAsync(oldToNew, AliasMoveOrigin.Edit, cancellationToken);

    public async Task<bool> MoveAliasesAsync(
        IReadOnlyDictionary<string, string> oldToNew,
        AliasMoveOrigin origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oldToNew);

        var command = new MoveRepositoryAliasesCommand(oldToNew, KeepStamp: origin is AliasMoveOrigin.Backstop);
        var moved = await _move.Handle(command, cancellationToken).ConfigureAwait(false);

        if (moved) changes.Raise();
        return moved;
    }
}
