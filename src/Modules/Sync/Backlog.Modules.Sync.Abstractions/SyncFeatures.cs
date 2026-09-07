namespace Backlog.Modules.Sync.Abstractions;

/// <summary>The feature keys the Sync context owns.</summary>
public static class SyncFeatures
{
    /// <summary>Register this device with the sync service, and pair a second
    /// one to it.</summary>
    public const string DevicePairing = "device-pairing";

    /// <summary>Push local task changes to the replica and pull the owner's
    /// change feed back. Separate from <see cref="DevicePairing"/> because a
    /// person can have paired devices and still not want their tasks leaving
    /// the machine.</summary>
    public const string TaskSync = "task-sync";
}
