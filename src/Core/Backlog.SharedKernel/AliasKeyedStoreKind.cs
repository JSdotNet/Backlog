namespace Backlog.SharedKernel;

/// <summary>
/// Whether an alias-keyed store travels between devices (local ADR 0060 §3), which
/// decides who re-keys it when a repository is relabelled.
/// </summary>
public enum AliasKeyedStoreKind
{
    /// <summary>Replicated between paired devices: the device that made the edit
    /// re-keys it, and the re-keyed document travels like any other change.</summary>
    Replicated,

    /// <summary>Kept on this device alone: every device re-keys its own copy from the
    /// registry's alias moves, once per move group.</summary>
    Local
}
