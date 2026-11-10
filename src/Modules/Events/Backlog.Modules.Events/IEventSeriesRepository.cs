using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events;

/// <summary>
/// Local-first persistence for the <see cref="EventSeries"/> aggregate: whole-series load and
/// save (guideline ADR 0014). Its own port beside <see cref="IKnowledgeEventRepository"/>,
/// because a series is its own aggregate: editing one never rewrites its editions.
/// <para>
/// Not registered by <c>AddEventsModule()</c>: the host picks the adapter, as for knowledge
/// events.
/// </para>
/// </summary>
public interface IEventSeriesRepository
{
    /// <summary>One series, or null when none has that id.</summary>
    Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every series.</summary>
    Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the whole series, atomically from a reader's point of view.</summary>
    Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default);
}
