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
/// </summary>
public interface IKnowledgeEventRepository
{
    /// <summary>One knowledge event, or null when none has that id.</summary>
    Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every knowledge event, in the order they were first saved.</summary>
    Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the whole event, atomically from a reader's point of view.</summary>
    Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default);
}
