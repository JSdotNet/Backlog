using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events;

/// <summary>A stored event series with the stamp its row carries.</summary>
public sealed record StoredEventSeries(EventSeries Series, DateTimeOffset UpdatedAt);

/// <summary>
/// <see cref="IKnowledgeEventReplicaStore"/>'s terms for the series beside
/// <see cref="IEventSeriesRepository"/>: series ride the same feed as the events that name
/// them, so a phone that holds an event can show its series. Not registered by
/// <c>AddEventsModule()</c>.
/// </summary>
public interface IEventSeriesReplicaStore
{
    /// <summary>Every series whose stamp is later than <paramref name="since"/>, oldest stamp
    /// first, each with its stamp. A row this build cannot read is left out.</summary>
    Task<IReadOnlyList<StoredEventSeries>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Writes <paramref name="series"/> whole at <paramref name="updatedAt"/> when the
    /// store holds no copy or one with an earlier stamp, in one step; answers whether it
    /// wrote.</summary>
    Task<bool> SaveReplicatedAsync(EventSeries series, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
}
