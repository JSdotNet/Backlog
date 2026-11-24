namespace Backlog.SharedKernel;

/// <summary>
/// Why a replicated alias-keyed store is asked to move data (local ADR 0060 §3), which
/// decides how the moved copies are stamped.
/// </summary>
public enum AliasMoveOrigin
{
    /// <summary>This device made the relabel. The moved copies are a new version,
    /// stamped now, so they travel and win over every older copy.</summary>
    Edit,

    /// <summary>The backstop found data under an alias no row carries. The moved copies
    /// keep their own stamps: a copy this device pulled loses to the editing device's
    /// re-keyed one, and a write it has not pushed yet still travels.</summary>
    Backstop
}
