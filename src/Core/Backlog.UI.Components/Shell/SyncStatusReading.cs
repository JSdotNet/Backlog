namespace Backlog.UI.Components.Shell;

/// <summary>Which of the three things a device's sync can be doing.</summary>
public enum SyncStatusKind
{
    /// <summary>The device has no credential, so there is nothing to sync with.</summary>
    NotPaired,

    /// <summary>Paired and reachable. <see cref="SyncStatusReading.LastSyncedAt"/>
    /// says when it last completed, or is null when it has not yet.</summary>
    Synced,

    /// <summary>Paired, and the service cannot be reached.
    /// <see cref="SyncStatusReading.Waiting"/> is what is queued for when it can.</summary>
    Offline
}

/// <summary>
/// One reading of a device's sync, for <c>SyncStatusLine</c> to put into words.
/// The line is handed a reading rather than asking for one, so the library knows
/// nothing about where sync state comes from and a storybook can draw every
/// state without a service behind it.
/// </summary>
public sealed record SyncStatusReading(SyncStatusKind Kind, DateTimeOffset? LastSyncedAt = null, int Waiting = 0)
{
    public static SyncStatusReading NotPaired { get; } = new(SyncStatusKind.NotPaired);

    public static SyncStatusReading Synced(DateTimeOffset? at) => new(SyncStatusKind.Synced, at);

    public static SyncStatusReading Offline(int waiting, DateTimeOffset? lastSyncedAt = null) =>
        new(SyncStatusKind.Offline, lastSyncedAt, Math.Max(0, waiting));
}
