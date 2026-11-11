using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>The knowledge event replica store, in memory, applying the store's rule the way the
/// SQLite adapter does: a copy is written only when its stamp is later than the held one, and
/// then at that stamp.</summary>
internal sealed class InMemoryKnowledgeEventReplicaStore : IKnowledgeEventReplicaStore
{
    private readonly Dictionary<Guid, StoredKnowledgeEvent> _rows = [];

    public StoredKnowledgeEvent? Find(Guid id) => _rows.GetValueOrDefault(id);

    /// <summary>What this machine holds, as if the pane had saved it at <paramref name="at"/>.</summary>
    public KnowledgeEvent Seed(KnowledgeEvent knowledgeEvent, DateTimeOffset at)
    {
        _rows[knowledgeEvent.Id] = new StoredKnowledgeEvent(knowledgeEvent, at);
        return knowledgeEvent;
    }

    public Task<IReadOnlyList<StoredKnowledgeEvent>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredKnowledgeEvent>>(
            [.. _rows.Values.Where(row => row.UpdatedAt > since).OrderBy(row => row.UpdatedAt).ThenBy(row => row.Event.Id)]);

    public Task<bool> SaveReplicatedAsync(KnowledgeEvent knowledgeEvent, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        if (_rows.TryGetValue(knowledgeEvent.Id, out var held) && updatedAt <= held.UpdatedAt) return Task.FromResult(false);

        _rows[knowledgeEvent.Id] = new StoredKnowledgeEvent(knowledgeEvent, updatedAt);
        return Task.FromResult(true);
    }
}

/// <summary>The event series replica store, in memory, on the same rule.</summary>
internal sealed class InMemoryEventSeriesReplicaStore : IEventSeriesReplicaStore
{
    private readonly Dictionary<Guid, StoredEventSeries> _rows = [];

    public StoredEventSeries? Find(Guid id) => _rows.GetValueOrDefault(id);

    public EventSeries Seed(EventSeries series, DateTimeOffset at)
    {
        _rows[series.Id] = new StoredEventSeries(series, at);
        return series;
    }

    public Task<IReadOnlyList<StoredEventSeries>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredEventSeries>>(
            [.. _rows.Values.Where(row => row.UpdatedAt > since).OrderBy(row => row.UpdatedAt).ThenBy(row => row.Series.Id)]);

    public Task<bool> SaveReplicatedAsync(EventSeries series, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        if (_rows.TryGetValue(series.Id, out var held) && updatedAt <= held.UpdatedAt) return Task.FromResult(false);

        _rows[series.Id] = new StoredEventSeries(series, updatedAt);
        return Task.FromResult(true);
    }
}
