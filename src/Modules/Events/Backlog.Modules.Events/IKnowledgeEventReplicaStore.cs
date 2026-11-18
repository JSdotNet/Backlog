using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events;

/// <summary>A stored knowledge event with the stamp its row carries: when a desktop last
/// changed it, on that desktop's clock.</summary>
public sealed record StoredKnowledgeEvent(KnowledgeEvent Event, DateTimeOffset UpdatedAt);

/// <summary>
/// What knowledge event replication needs of the store beside
/// <see cref="IKnowledgeEventRepository"/>: the events saved since a watermark, and a write
/// that keeps the stamp a copy from another desktop arrived with (local ADR 0050 §2, the
/// latest write wins). Its own port rather than two more members on the repository, so the
/// module's other services and their test doubles never see replication.
/// <para>
/// Not registered by <c>AddEventsModule()</c>: the host's adapter answers it beside the
/// repository, as <c>IGoalReplicaStore</c> is answered beside <c>IGoalRepository</c>.
/// </para>
/// </summary>
public interface IKnowledgeEventReplicaStore
{
    /// <summary>Every event whose stamp is later than <paramref name="since"/>, oldest stamp
    /// first, each with its stamp. A row this build cannot read is left out.</summary>
    Task<IReadOnlyList<StoredKnowledgeEvent>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="knowledgeEvent"/> whole at <paramref name="updatedAt"/> — the
    /// stamp it arrived with, not this machine's clock, so the next push does not offer it as
    /// a newer change — when the store holds no copy or one with an earlier stamp. Answers
    /// whether it wrote. The comparison and the write are one step, so a change saved on this
    /// machine meanwhile is never overwritten by an older copy.
    /// </summary>
    Task<bool> SaveReplicatedAsync(KnowledgeEvent knowledgeEvent, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);
}
