using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>The two roadmap documents that travel between a person's devices
/// (local ADR 0018): the whole plan, and the pace it is drawn at. Two rather than
/// one, so a pace changed on one PC and a plan edited on another both
/// survive.</summary>
public enum RoadmapReplicaDocument
{
    Plan,
    Pace,
}

/// <summary>What <see cref="IRoadmapReplication.ApplyAsync"/> did with an inbound
/// copy. Four answers rather than a bool, because the sync client counts the
/// first as work applied, the last as a document this build could not read, and
/// the two between as nothing to do.</summary>
public enum RoadmapReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has: this
    /// device's own push coming back, or a replayed page. Nothing was
    /// written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused, as the
    /// replica refuses a stale push.</summary>
    Refused,

    /// <summary>The copy does not read as the document it claims to be, or this
    /// head keeps no such document. The local one was left exactly as it
    /// was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands the replicated roadmap documents to, and reads
/// them from (local ADR 0018).
/// <para>
/// The client routes by kind and nothing else: it carries a document's text and
/// its stamp and never looks inside. Which copy wins, and whether a copy can be
/// read at all, is decided here — the same arrangement the Inbox's
/// <c>IInboxIntake</c> has for captures. A head without a roadmap composes the
/// client without this port, and both documents stay on the replica.
/// </para>
/// </summary>
public interface IRoadmapReplication
{
    /// <summary>
    /// Raised after the plan or the pace was written on this machine — a person's
    /// edit, an import, a re-lengthening — so a sync loop can push it within
    /// seconds rather than on its next tick.
    /// <para>
    /// Never for a copy <see cref="ApplyAsync"/> took: a document that arrived
    /// from the replica is not something to tell the replica about, and a loop
    /// that heard it as a local change would start a cycle on the heels of every
    /// cycle that received one (<c>ITaskChangeSignal.Suppress</c> gives the same
    /// reason for tasks). It may arrive on any thread.
    /// </para>
    /// </summary>
    event Action? Changed;

    /// <summary>The document as this device holds it, or <c>null</c> when it was
    /// never saved here — so a device that never planned has nothing to send,
    /// and never replaces another device's plan with an empty one.</summary>
    Task<RoadmapReplicaCopyDto?> ReadAsync(
        RoadmapReplicaDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers a copy that arrived from another device. A later stamp is taken
    /// whole — the stored text replaced verbatim, with the inbound stamp rather
    /// than this machine's clock — and the roadmap redraws. An equal stamp is an
    /// echo and an older one is refused; neither writes anything. A copy that does
    /// not read as the document it claims to be leaves the local one as it was.
    /// </summary>
    Task<RoadmapReplicaOutcome> ApplyAsync(
        RoadmapReplicaDocument document,
        RoadmapReplicaCopyDto inbound,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Brings the plan and the pace up to date with the person's other devices before
/// the roadmap first reads them, where the host replicates at all (local ADR
/// 0018, Consequences).
/// <para>
/// A port the host answers over its sync loop, because a module may not reference
/// infrastructure. A host that does not replicate registers none, and the plan is
/// read at once. The wait is bounded by the answerer: a device that is offline or
/// slow reads what it holds rather than keeping the roadmap blank.
/// </para>
/// </summary>
public interface IRoadmapCatchUp
{
    /// <summary>Completes once this device has pulled, or has given up waiting
    /// for a pull. Never throws for a failed pull: the roadmap draws what it has
    /// either way.</summary>
    Task CatchUpAsync(CancellationToken cancellationToken = default);
}
