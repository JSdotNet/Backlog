using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Replication;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The terms a Progress document travels between a person's devices on: each one a whole
/// document under its own id, the newer write winning per document (local ADR 0035 §9,
/// on the terms of local ADR 0018). <see cref="IGoalReplication"/> and
/// <see cref="IImpedimentReplication"/> are this port for their document.
/// <para>
/// The client routes by kind and never looks inside; which copy wins, and whether a copy
/// can be read at all, is decided behind the port.
/// </para>
/// </summary>
/// <typeparam name="TCopy">One copy of the document as it travels.</typeparam>
public interface IWholeDocumentReplication<TCopy>
    where TCopy : class, IWholeDocumentCopy
{
    /// <summary>Raised after a document was written on this machine by a person's change,
    /// so a sync loop can push it within seconds. Never for a copy
    /// <see cref="ApplyAsync"/> took. It may arrive on any thread.</summary>
    event Action? Changed;

    /// <summary>Every document this device holds, each exactly as stored with its stamp —
    /// a deleted goal's tombstone included. Empty on a device that never set one.</summary>
    Task<IReadOnlyList<TCopy>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Offers a copy that arrived from another device. A later stamp is taken
    /// whole, verbatim, at the inbound stamp; an equal one is an echo and an older one is
    /// refused, neither writing anything. A copy that does not read as the document it
    /// claims to be leaves the local one as it was.</summary>
    Task<ReplicaOutcome> ApplyAsync(TCopy inbound, CancellationToken cancellationToken = default);
}
