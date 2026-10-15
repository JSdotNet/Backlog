using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>What <see cref="IMyDayPreferencesReplication.ApplyAsync"/> did with an
/// inbound copy. Four answers rather than a bool, for the reason the roadmap's
/// replication gives: the sync client counts the first as work applied, the last as a
/// document this build could not read, and the two between as nothing to do.</summary>
public enum MyDayPreferencesReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it whole.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has: this device's
    /// own push coming back, or a replayed page. Nothing was written.</summary>
    Echo,

    /// <summary>The copy is older than the local one and was refused, as the replica
    /// refuses a stale push.</summary>
    Refused,

    /// <summary>The copy does not read as the preferences document. The local one was
    /// left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands the replicated My Day preferences to, and reads
/// them from (local ADR 0032, §2–§3): one whole document, the later stamp winning, on
/// the terms Roadmap's <c>IRoadmapReplication</c> keeps for the pace.
/// <para>
/// The client routes by kind and never looks inside. A head without the port — an
/// older desktop build — skips the document, and the phone, which only reads it, folds
/// it itself.
/// </para>
/// </summary>
public interface IMyDayPreferencesReplication
{
    /// <summary>
    /// Raised after the person changed the preferences on this device, so a sync loop
    /// can push them within seconds rather than on its next tick.
    /// <para>
    /// Never for a copy <see cref="ApplyAsync"/> took: a document that arrived from the
    /// replica is not something to tell the replica about, and a loop that heard it as
    /// a local change would push on the heels of every pull that brought one. The
    /// screen hears a taken copy through <see cref="IMyDayPreferences.Changed"/>
    /// instead. It may arrive on any thread.
    /// </para>
    /// </summary>
    event Action? Changed;

    /// <summary>The document as this device holds it, or <c>null</c> when it was never
    /// saved here — so a device that never set the switch sends nothing, and never
    /// replaces another device's choice with the default.</summary>
    Task<MyDayPreferencesCopyDto?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers a copy that arrived from another device. A later stamp is taken whole —
    /// the stored text replaced verbatim, keys this build does not know included, at
    /// the inbound stamp rather than this machine's clock. An equal stamp is an echo
    /// and an older one is refused; neither writes anything. A copy that does not read
    /// as the preferences document leaves the local one as it was.
    /// </summary>
    Task<MyDayPreferencesReplicaOutcome> ApplyAsync(
        MyDayPreferencesCopyDto inbound,
        CancellationToken cancellationToken = default);
}
