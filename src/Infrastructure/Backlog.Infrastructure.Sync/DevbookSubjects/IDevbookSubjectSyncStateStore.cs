namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>
/// How far this device has got in each direction of devbook subject
/// replication — <see cref="Annotations.AnnotationSyncState"/>'s values over
/// the subject feed, under the same rules: the watermark advances to what was
/// accepted and never to "now", a null cursor means "from the beginning", and
/// the owner and device the progress was recorded under make a device that
/// registers again start this feed over.
/// </summary>
public sealed record DevbookSubjectSyncState(
    DateTimeOffset PushWatermark,
    string? PullCursor,
    Guid? OwnerId = null,
    Guid? DeviceId = null);

/// <summary>
/// Where this device keeps its devbook subject replication progress between
/// runs. Its own store rather than fields on another feed's, for the reason
/// <see cref="Annotations.IAnnotationSyncStateStore"/> keeps its own: one file
/// carrying two feeds would make them fail together.
/// </summary>
public interface IDevbookSubjectSyncStateStore
{
    /// <summary>Where this device has got to, or a state that has got nowhere.</summary>
    DevbookSubjectSyncState Current { get; }

    /// <summary>Records the state, replacing what was there, and raises
    /// <see cref="Changed"/>. Throws and leaves <see cref="Current"/> alone when
    /// it cannot record it.</summary>
    void Save(DevbookSubjectSyncState state);

    /// <summary>Where the progress is kept, for a settings screen to show.</summary>
    string StorePath { get; }

    /// <summary>Raised after <see cref="Save"/>.</summary>
    event Action? Changed;
}
