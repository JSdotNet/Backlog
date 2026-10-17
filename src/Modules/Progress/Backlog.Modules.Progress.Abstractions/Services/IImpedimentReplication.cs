using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>What <see cref="IImpedimentReplication.ApplyAsync"/> did with an inbound copy
/// of an impediment — the four answers <see cref="GoalReplicaOutcome"/> gives a goal.</summary>
public enum ImpedimentReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has. Nothing was written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused.</summary>
    Refused,

    /// <summary>The copy does not read as an impediment, names another one than it claims,
    /// or this head keeps no impediments. The local one was left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands replicated impediments to, and reads them from: one
/// whole <c>impediment</c> document per impediment, the newer write winning per document
/// (<c>.devbook/domain/progress/features.md#syncing-goals-and-impediments</c>, local ADR
/// 0035 §9) — the terms <see cref="IGoalReplication"/> gives a goal. Not behind the
/// <see cref="ProgressFeatures.Goals"/> switch: impediments recorded while it is off are
/// kept, so they travel too.
/// </summary>
public interface IImpedimentReplication
{
    /// <summary>Raised after an impediment was written on this machine by a local change,
    /// so a sync loop can push it within seconds. Never for a copy
    /// <see cref="ApplyAsync"/> took. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Every impediment this device holds, each exactly as stored with its stamp.</summary>
    Task<IReadOnlyList<ImpedimentReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Offers a copy that arrived from another device. A later stamp is taken
    /// whole, verbatim; an equal one is an echo and an older one is refused, neither
    /// writing anything.</summary>
    Task<ImpedimentReplicaOutcome> ApplyAsync(ImpedimentReplicaCopyDto inbound, CancellationToken cancellationToken = default);
}
