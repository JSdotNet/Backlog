namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>
/// How far this device has got in each direction of annotation replication —
/// <see cref="TaskSyncState"/>'s two values over the third feed, under the same
/// rules: the watermark advances to what was accepted and never to "now", and a
/// null cursor means "from the beginning" — and, for the same reason as there,
/// the owner and device the progress was recorded under, so a device that
/// registers again starts this feed over as well.
/// </summary>
public sealed record AnnotationSyncState(
    DateTimeOffset PushWatermark,
    string? PullCursor,
    Guid? OwnerId = null,
    Guid? DeviceId = null);

/// <summary>
/// Where this device keeps its annotation-replication progress between runs.
/// <para>
/// Its own store rather than two more fields on <see cref="ITaskSyncStateStore"/>,
/// for the reason session replication keeps its own: one file carrying both
/// would make the two exchanges fail together, and a corrupt save from one loop
/// would reset the other's watermark and re-push the whole machine. The
/// contract is otherwise <see cref="ITaskSyncStateStore"/>'s, including what
/// forgetting either value means and why re-pushing is free.
/// </para>
/// </summary>
public interface IAnnotationSyncStateStore
{
    /// <summary>Where this device has got to, or a state that has got nowhere.</summary>
    AnnotationSyncState Current { get; }

    /// <summary>Records the state, replacing what was there, and raises
    /// <see cref="Changed"/>. Throws and leaves <see cref="Current"/> alone when
    /// it cannot record it.</summary>
    void Save(AnnotationSyncState state);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
