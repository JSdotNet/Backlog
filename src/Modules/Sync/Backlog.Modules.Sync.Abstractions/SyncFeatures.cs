namespace Backlog.Modules.Sync.Abstractions;

/// <summary>The feature keys the Sync context owns.</summary>
public static class SyncFeatures
{
    /// <summary>Register this device with the sync service, and pair a second
    /// one to it.</summary>
    public const string DevicePairing = "device-pairing";
}
