using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>What <see cref="ISprintSeriesReplication.ApplyAsync"/> did with an inbound
/// copy of a series. Four answers rather than a bool, for the reason the roadmap's
/// replication gives: the sync client counts the first as work applied, the last as a
/// document this build could not read, and the two between as nothing to do.</summary>
public enum SprintSeriesReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has: this device's
    /// own push coming back, or a replayed page. Nothing was written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused, as the replica
    /// refuses a stale push.</summary>
    Refused,

    /// <summary>The copy does not read as a sprint series, names another series than
    /// the one it claims, or this head keeps no series. The local one was left exactly
    /// as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands replicated sprint series to, and reads them from:
/// one whole document per series, the newer write winning per series, on the terms
/// Roadmap Planning's plan travels on (local ADR 0018).
/// <para>
/// A series is the unit because it is the consistency boundary: its cadence, naming and
/// goal rules, every sprint, which tasks and plans belong to each, the goal marks, notes
/// and open questions are one document, so closing a sprint — which changes two sprints
/// at once — can never arrive half. The client routes by kind and never looks inside;
/// which copy wins, and whether a copy can be read at all, is decided here.
/// </para>
/// <para>
/// A head without Progress composes the client without this port, and the series stay
/// on the replica.
/// </para>
/// </summary>
public interface ISprintSeriesReplication
{
    /// <summary>
    /// Raised after a series was written on this machine by a person's change, so a
    /// sync loop can push it within seconds rather than on its next tick. Never for a
    /// copy <see cref="ApplyAsync"/> took, for the reason the roadmap's notice gives: a
    /// series that arrived from the replica is not something to tell the replica about.
    /// It may arrive on any thread.
    /// </summary>
    event Action? Changed;

    /// <summary>Every series this device holds, each exactly as stored with its stamp.
    /// Empty on a device that never set one up, so it never replaces another device's
    /// series with nothing.</summary>
    Task<IReadOnlyList<SprintSeriesReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers a copy that arrived from another device. A later stamp is taken whole —
    /// the stored text replaced verbatim, at the inbound stamp rather than this
    /// machine's clock. An equal stamp is an echo and an older one is refused; neither
    /// writes anything. A copy that does not read as the series it claims to be leaves
    /// the local one as it was.
    /// </summary>
    Task<SprintSeriesReplicaOutcome> ApplyAsync(
        SprintSeriesReplicaCopyDto inbound,
        CancellationToken cancellationToken = default);
}
