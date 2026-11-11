using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// Whether a pushed copy of a knowledge event or series may replace the one the replica
/// holds.
/// <para>
/// <b>A push may never move a document backwards</b>, for the reason
/// <see cref="DevbookSubjectChangePrecedence"/> records: every desktop pushes everything
/// above its own watermark, a copy it pulled sits there exactly as one it edited does, and
/// under a blind upsert its echo would put back the copy another desktop had moved on from.
/// The rule is on the device stamp, which an echo carries unchanged.
/// </para>
/// <para>
/// An event is never deleted on the feed, so a tie is the same version twice — or two
/// desktops that changed it in the same tick, which nothing can order — and changes nothing.
/// A copy rather than a shared generic rule, as each feed's is; shared by both adapters, so
/// the in-memory store the endpoint tests run against and the Cosmos store cannot disagree.
/// </para>
/// </summary>
public static class KnowledgeEventChangePrecedence
{
    /// <summary>True when <paramref name="inbound"/> is a later version than
    /// <paramref name="stored"/> and should be written over it, whole.</summary>
    public static bool Supersedes(KnowledgeEventChange inbound, KnowledgeEventChange stored)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(stored);

        return inbound.UpdatedAt > stored.UpdatedAt;
    }
}
