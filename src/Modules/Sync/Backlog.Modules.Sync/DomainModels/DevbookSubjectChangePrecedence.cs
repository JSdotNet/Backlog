using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// Whether a pushed copy of a devbook subject's state may replace the one the
/// replica holds.
/// <para>
/// <b>A push may never move a document backwards</b>, for the whole of the
/// reason <see cref="AnnotationChangePrecedence"/> records: every device pushes
/// everything above its own watermark, a state it pulled sits there exactly as
/// one it set does, and under a blind upsert its echo would put back the state
/// another device had moved on from. The rule is on the device stamp, which an
/// echo carries unchanged.
/// </para>
/// <para>
/// Simpler than the annotation rule by the tombstone: a subject's state is set
/// and never deleted, so a tie is the same version twice — or two devices that
/// set it in the same tick, which nothing can order — and changes nothing. A
/// copy rather than a shared generic rule, on the terms local ADR 0011 sets for
/// the annotation pipeline; shared by both adapters, so the in-memory store the
/// endpoint tests run against and the Cosmos store cannot disagree.
/// </para>
/// </summary>
public static class DevbookSubjectChangePrecedence
{
    /// <summary>True when <paramref name="inbound"/> is a later version than
    /// <paramref name="stored"/> and should be written over it, whole.</summary>
    public static bool Supersedes(DevbookSubjectChange inbound, DevbookSubjectChange stored)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(stored);

        return inbound.UpdatedAt > stored.UpdatedAt;
    }
}
