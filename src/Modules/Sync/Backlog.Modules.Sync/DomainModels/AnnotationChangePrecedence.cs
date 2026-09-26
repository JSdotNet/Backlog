using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// Whether a pushed copy of an annotation document may replace the one the
/// replica holds.
/// <para>
/// <b>A push may never move a document backwards.</b> The task replica's rule
/// (<c>TaskChangePrecedence</c>), over the third container, because the
/// annotation exchange is the task exchange's copy and so was its bug: every
/// device pushes everything above its own watermark, and a document it
/// <em>received</em> sits above that watermark exactly as one it edited does —
/// so each device echoes every remark it pulled, once, on its next push. Under
/// a blind upsert that echo replaced whatever the replica had learned in
/// between: a tombstone one desktop had pushed came back as the live remark
/// the other was still holding, and an edit made on one desktop was reverted
/// to the copy the other had pulled an hour earlier. The first desktop then
/// took the older copy over its own pushed one, because the replica is
/// authoritative for anything a device has already sent
/// (<c>AnnotationReplicaMerge.ShouldApply</c>).
/// </para>
/// <para>
/// The rule is on the device stamp and not on arrival order, because an echo
/// carries the stamp of the version it is echoing — it is the one thing that
/// says "this is not new" whatever the pushing device's clock reads. On a tie a
/// tombstone beats the live copy it replaced, and an identical pair is the same
/// version twice and changes nothing. What this costs is the genuine race
/// arrival order resolved: two desktops editing one remark in the same interval
/// now resolve to the later-stamped edit rather than the later-uploaded one.
/// Both lose one edit; only one of them also loses every deletion.
/// </para>
/// <para>
/// A copy of the task rule rather than a shared generic one, on the terms
/// .devbook/arc42/adr/0011 sets for the whole annotation pipeline: the two change
/// records share a shape and not a type, and the rule is four lines. Shared by
/// both annotation adapters so the in-memory store every endpoint test runs
/// against and the Cosmos store the service deploys with cannot disagree about
/// what a stale push is.
/// </para>
/// </summary>
public static class AnnotationChangePrecedence
{
    /// <summary>True when <paramref name="inbound"/> is a later version than
    /// <paramref name="stored"/> and should be written over it.</summary>
    public static bool Supersedes(AnnotationChange inbound, AnnotationChange stored)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(stored);

        if (inbound.UpdatedAt != stored.UpdatedAt)
        {
            return inbound.UpdatedAt > stored.UpdatedAt;
        }

        // Same stamp: only a deletion of the very version held says anything new.
        return inbound.DeletedAt is not null && stored.DeletedAt is null;
    }
}
