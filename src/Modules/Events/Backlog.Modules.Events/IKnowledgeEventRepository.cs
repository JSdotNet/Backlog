using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events;

/// <summary>
/// Local-first persistence for the <see cref="KnowledgeEvent"/> aggregate: whole-event load and
/// save, because a knowledge event is one consistency boundary (guideline ADR 0014).
/// <para>
/// A port, deliberately not registered by <c>AddEventsModule()</c>: which adapter implements it
/// is the host's decision — one row per event in the SQLite database the tasks live in, as local
/// ADR 0003 does for tasks and ADR 0050 §2 asks of the module's own documents.
/// </para>
/// <para>
/// A removed event (<see cref="KnowledgeEvent.IsRemoved"/>) stays stored as a tombstone, so the
/// removal replicates through <see cref="IKnowledgeEventReplicaStore"/>; neither
/// <see cref="GetAsync"/> nor <see cref="ListAsync"/> answers it.
/// </para>
/// </summary>
public interface IKnowledgeEventRepository
{
    /// <summary>One knowledge event, or null when none has that id or it was removed.</summary>
    Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every knowledge event not removed, in the order they were first saved.</summary>
    Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the store holds the event with that id as a removed event's tombstone: what tells
    /// an event removed here from one that has not arrived yet, which <see cref="GetAsync"/>
    /// answers alike. False for an id it holds no row for. The default answers false, for a
    /// store that keeps no tombstones.
    /// </summary>
    Task<bool> IsRemovedAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>Writes the whole event, atomically from a reader's point of view; a removed one
    /// is written as its tombstone.</summary>
    Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default);
}
