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

    /// <summary>Push this machine's session records to the replica and pull back
    /// what the other environments reported. Its own key rather than a second
    /// use of <see cref="TaskSync"/>, because the two answer different questions
    /// about the same person: tasks are their work, and a session record is a
    /// note about how they did it. Somebody can want their backlog on both
    /// machines and still not want a list of what their agents have been doing
    /// leaving either one — and the sanitization boundary that makes the second
    /// safe (.arc42/adr/0005 §Session records) is not the same argument as the
    /// one that makes the first safe, so it deserves its own switch to say no
    /// to.</summary>
    public const string SessionSync = "session-sync";
}
