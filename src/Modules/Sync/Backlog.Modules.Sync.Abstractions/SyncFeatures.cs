namespace Backlog.Modules.Sync.Abstractions;

/// <summary>The feature keys the Sync context owns.</summary>
public static class SyncFeatures
{
    /// <summary>Talk to the cloud sync service at all: register this device and
    /// pair a second one to it, push local task changes to the replica and pull
    /// the owner's change feed back, and do the same for this machine's session
    /// records - only their metadata, never a prompt, a transcript, a working
    /// folder, or a title (.arc42/adr/0005 §Session records).
    /// <para>
    /// One switch where there used to be three (<c>device-pairing</c>,
    /// <c>task-sync</c>, <c>session-sync</c>). They were split so that somebody
    /// could pair devices without their tasks leaving the machine, or replicate
    /// their backlog without a note of what their agents had been doing going
    /// with it. In practice the question a person answers on the settings
    /// screen is whether this machine takes part in sync, and three switches
    /// that each had to be found and turned on before anything synced made that
    /// one answer look like three. The sanitization boundary that made session
    /// records safe to replicate still holds; it is what lets the three become
    /// one rather than a reason to keep them apart. The former keys are named
    /// on the catalog entry, so a settings file written while any of them was on
    /// comes up with this one on.
    /// </para></summary>
    public const string Sync = "sync";
}
