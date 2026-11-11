namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// How far this device has got in each direction of knowledge event replication —
/// <see cref="DevbookSubjects.DevbookSubjectSyncState"/>'s values over the knowledge event feed,
/// under the same rules: the watermark advances to what was accepted and never to "now", a null
/// cursor means "from the beginning", and the owner and device the progress was recorded under
/// make a device that registers again start this feed over.
/// </summary>
public sealed record KnowledgeEventSyncState(
    DateTimeOffset PushWatermark,
    string? PullCursor,
    Guid? OwnerId = null,
    Guid? DeviceId = null);

/// <summary>
/// Where this device keeps its knowledge event replication progress between runs. Its own
/// store rather than fields on another feed's, so the feeds fail separately.
/// </summary>
public interface IKnowledgeEventSyncStateStore
{
    /// <summary>Where this device has got to, or a state that has got nowhere.</summary>
    KnowledgeEventSyncState Current { get; }

    /// <summary>Records the state, replacing what was there, and raises <see cref="Changed"/>.
    /// Throws and leaves <see cref="Current"/> alone when it cannot record it.</summary>
    void Save(KnowledgeEventSyncState state);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
