using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>What <see cref="IGoalReplication.ApplyAsync"/> did with an inbound copy of a
/// goal: the sync client counts the first as work applied, the last as a
/// document this build could not read, and the two between as nothing to do.</summary>
public enum GoalReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has. Nothing was written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused.</summary>
    Refused,

    /// <summary>The copy does not read as a goal, names another goal than the one it
    /// claims, or this head keeps no goals. The local one was left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands replicated goals to, and reads them from: one whole
/// <c>goal</c> document per goal, the newer write winning per goal
/// (<c>.devbook/domain/progress/features.md#syncing-goals-and-impediments</c>, local ADR
/// 0035 §9) — the terms the roadmap's plan travels on (local ADR 0018).
/// <para>
/// A goal is the unit because it is the consistency boundary for its attachments. The
/// client routes by kind and never looks inside; which copy wins, and whether a copy can
/// be read at all, is decided here. A deleted goal travels as its tombstone.
/// </para>
/// </summary>
public interface IGoalReplication
{
    /// <summary>Raised after a goal was written on this machine by a person's change, so a
    /// sync loop can push it within seconds. Never for a copy <see cref="ApplyAsync"/>
    /// took. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Every goal this device holds, deleted ones' tombstones included, each
    /// exactly as stored with its stamp. Empty on a device that never set one.</summary>
    Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Offers a copy that arrived from another device. A later stamp is taken
    /// whole, verbatim, at the inbound stamp; an equal one is an echo and an older one is
    /// refused, neither writing anything. A copy that does not read as the goal it claims
    /// to be leaves the local one as it was.</summary>
    Task<GoalReplicaOutcome> ApplyAsync(GoalReplicaCopyDto inbound, CancellationToken cancellationToken = default);
}
