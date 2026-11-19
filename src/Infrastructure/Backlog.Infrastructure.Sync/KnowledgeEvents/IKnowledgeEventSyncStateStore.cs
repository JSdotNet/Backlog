namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// How far this device has got in each direction of knowledge event replication —
/// <see cref="DevbookSubjects.DevbookSubjectSyncState"/>'s values over the knowledge event feed,
/// under the same rules: the watermark advances to what was accepted and never to "now", a null
/// cursor means "from the beginning", and the owner and device the progress was recorded under
/// make a device that registers again start this feed over.
/// <para>
/// <paramref name="Area"/> is this desktop's <c>events</c> switch as last seen, and whether its
/// move has reached the replica yet (local ADR 0050 §6). Null until the switch is first seen.
/// </para>
/// </summary>
public sealed record KnowledgeEventSyncState(
    DateTimeOffset PushWatermark,
    string? PullCursor,
    Guid? OwnerId = null,
    Guid? DeviceId = null,
    EventsAreaSwitch? Area = null);

/// <summary>
/// This desktop's <c>events</c> switch: where it stands, when it last moved, and whether that
/// move was sent. Only a move is sent: the first sight of the switch is recorded as sent, so a
/// desktop that upgrades never overrides a switch another desktop moved.
/// </summary>
/// <param name="On">Whether the area is on here.</param>
/// <param name="At">When the switch moved, or was first seen.</param>
/// <param name="Sent">Whether the replica has the switch as it stands.</param>
public sealed record EventsAreaSwitch(bool On, DateTimeOffset At, bool Sent);

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

    /// <summary>Reads, changes and records the state as one step no other <see cref="Save"/> or
    /// <see cref="Update"/> interleaves with — the worker records the <c>events</c> switch while a
    /// cycle records its progress, and neither may write back the other's old value. Answers the
    /// state recorded; a <paramref name="change"/> that answers the state it was given records
    /// nothing. Throws and leaves <see cref="Current"/> alone when it cannot record it.</summary>
    KnowledgeEventSyncState Update(Func<KnowledgeEventSyncState, KnowledgeEventSyncState> change);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
