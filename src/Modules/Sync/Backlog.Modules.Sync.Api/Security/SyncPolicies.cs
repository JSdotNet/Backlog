namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The authorization policy names endpoints reference. A policy name, never
/// policy logic: inherited ADR 0013 asks that the rule be registered once and
/// referenced by name, so that changing what "a paired device" means is one
/// edit rather than a search.
/// </summary>
public static class SyncPolicies
{
    /// <summary>A caller holding a live device token that names an owner.
    /// Every endpoint that touches an owner's data requires it.</summary>
    public const string PairedDevice = "sync:paired-device";
}
