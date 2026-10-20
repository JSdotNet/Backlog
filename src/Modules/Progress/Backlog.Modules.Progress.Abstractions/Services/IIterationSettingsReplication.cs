using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>What <see cref="IIterationSettingsReplication.ApplyAsync"/> did with an
/// inbound copy: the sync client counts the first as work applied, the last as a
/// document this build could not read, and the two between as nothing to do.</summary>
public enum IterationSettingsReplicaOutcome
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

    /// <summary>The copy does not read as the iteration settings document. The local
    /// one was left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands the replicated iteration settings to, and reads them
/// from (local ADR 0036, §3–§4): the length and the two-week start as one whole
/// document, the later stamp winning, on the terms the My Day preferences' port keeps
/// (local ADR 0032, §2).
/// <para>
/// The client routes by kind and never looks inside. A head without the port — an
/// older desktop build — skips the document, and the phone reads the iteration from the
/// stand-up context a desktop writes instead.
/// </para>
/// </summary>
public interface IIterationSettingsReplication
{
    /// <summary>
    /// Raised after the person changed the settings on this device, so a sync loop can
    /// push them within seconds rather than on its next tick.
    /// <para>
    /// Never for a copy <see cref="ApplyAsync"/> took: a document that arrived from the
    /// replica is not something to tell the replica about. The screen hears a taken copy
    /// through <see cref="IIterationSettings.Changed"/> instead. It may arrive on any
    /// thread.
    /// </para>
    /// </summary>
    event Action? Changed;

    /// <summary>The document as this device holds it, or <c>null</c> when it was never
    /// saved here — so a device that never chose an iteration sends nothing, and never
    /// replaces another device's choice with the default, Week.</summary>
    Task<IterationSettingsCopyDto?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers a copy that arrived from another device. A later stamp is taken whole —
    /// the stored text replaced verbatim, keys this build does not know included, at the
    /// inbound stamp rather than this machine's clock. An equal stamp is an echo and an
    /// older one is refused; neither writes anything. The one exception is the floor
    /// stamp a legacy value is seeded at (ADR 0036, §5): there an equal stamp carrying
    /// another choice is taken, so two seeded desktops converge. A copy that does not
    /// read as the settings leaves the local one as it was.
    /// </summary>
    Task<IterationSettingsReplicaOutcome> ApplyAsync(
        IterationSettingsCopyDto inbound,
        CancellationToken cancellationToken = default);
}
