using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>What one page did: how many documents were written.</summary>
public sealed record AnnotationMergeOutcome(int Applied);

/// <summary>
/// Reconciles a page of the owner's annotation feed with this device's own
/// store — <see cref="TaskReplicaMerge"/>'s rules over the annotation shape,
/// and deliberately the same rules, because the two devices have to agree on
/// them exactly.
/// <para>
/// Within a page, the later of two records for one annotation is decided by
/// the replica's stamp, then the writing device's <c>UpdatedAt</c>, then the
/// device id — so two devices never flap. Against the local copy, an arriving
/// version wins only when it is a later version — newer by <c>UpdatedAt</c>,
/// or a tombstone of the very version held — which is the rule the replica
/// applies to a push (<c>AnnotationChangePrecedence</c> in the Sync module).
/// An older version is refused whatever the push watermark says about the local
/// row; a local edit still waiting to be pushed is kept as a consequence, and
/// so is a row this device has already pushed. A tombstone is a version like
/// any other.
/// </para>
/// <para>
/// This class is also where the wire shape and the store's record meet, in
/// both directions, so "what leaves this machine" and "what is written into
/// it" have one place to be read.
/// </para>
/// </summary>
public sealed class AnnotationReplicaMerge
{
    private readonly IDevbookAnnotationStore _store;

    public AnnotationReplicaMerge(IDevbookAnnotationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <summary>The wire shape of one stored annotation, tombstone or live.</summary>
    public static AnnotationChange ToChange(DevbookAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return new AnnotationChange(
            annotation.Id,
            annotation.UpdatedAt,
            annotation.DeletedAt,
            new AnnotationPayload(
                annotation.RepositoryAlias,
                annotation.ChapterPath,
                annotation.BlockIndex,
                annotation.Body,
                annotation.Author,
                annotation.CreatedAt,
                annotation.Resolved));
    }

    /// <summary>The stored record for one arriving change.</summary>
    public static DevbookAnnotation ToAnnotation(AnnotationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var payload = change.Annotation;

        return new DevbookAnnotation(
            change.Id,
            payload.RepositoryAlias,
            payload.ChapterPath,
            payload.BlockIndex,
            payload.Body,
            payload.Author,
            payload.CreatedAt,
            change.UpdatedAt,
            payload.Resolved,
            change.DeletedAt);
    }

    /// <summary>Whether <paramref name="inbound"/> is the later of two records
    /// for the same annotation — the three-field rule, static and pure so it
    /// can be asserted on its own.</summary>
    public static bool Wins(AnnotationChangeRecord inbound, AnnotationChangeRecord against)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        ArgumentNullException.ThrowIfNull(against);

        if (inbound.ServerTimestamp != against.ServerTimestamp)
        {
            return inbound.ServerTimestamp > against.ServerTimestamp;
        }

        if (inbound.Change.UpdatedAt != against.Change.UpdatedAt)
        {
            return inbound.Change.UpdatedAt > against.Change.UpdatedAt;
        }

        return string.Compare(
            inbound.DeviceId.ToString("D"),
            against.DeviceId.ToString("D"),
            StringComparison.Ordinal) > 0;
    }

    /// <summary>
    /// Applies a page of the feed, reduced to one winner per annotation first,
    /// and answers how many documents it wrote. Nothing about this device's own
    /// progress is consulted — see <see cref="ShouldApply"/>.
    /// </summary>
    public AnnotationMergeOutcome Apply(IReadOnlyList<AnnotationChangeRecord> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;

        foreach (var record in Winners(page))
        {
            var local = _store.Find(record.Change.Id);

            if (!ShouldApply(record.Change, local)) continue;

            _store.Apply(ToAnnotation(record.Change));
            applied++;
        }

        return new AnnotationMergeOutcome(applied);
    }

    /// <summary>The apply-against-local decision on its own: word for word the
    /// replica's <c>AnnotationChangePrecedence.Supersedes</c>, over the local
    /// record instead of a stored change. A pull may never move a remark
    /// backwards, exactly as a push may not. The branch that used to take an
    /// older copy over a local row at or below the push watermark is gone, for
    /// the reason <c>TaskReplicaMerge.ApplyOneAsync</c> gives: it was written
    /// for a replica that kept whichever push arrived last, and with the replica
    /// refusing stale pushes it could only ever move a device backwards.</summary>
    internal static bool ShouldApply(AnnotationChange inbound, DevbookAnnotation? local)
    {
        if (local is null) return true;

        if (inbound.UpdatedAt != local.UpdatedAt)
        {
            return inbound.UpdatedAt > local.UpdatedAt;
        }

        // Same stamp: only a deletion of the very version held says anything
        // new. A tombstone and the live remark it replaced can share an
        // UpdatedAt only if one of them never happened, so the pair is read
        // together.
        return inbound.DeletedAt is not null && local.DeletedAt is null;
    }

    private static IEnumerable<AnnotationChangeRecord> Winners(IReadOnlyList<AnnotationChangeRecord> page)
    {
        var winners = new Dictionary<Guid, AnnotationChangeRecord>();

        foreach (var record in page)
        {
            if (!winners.TryGetValue(record.Change.Id, out var current) || Wins(record, current))
            {
                winners[record.Change.Id] = record;
            }
        }

        return winners.Values;
    }
}
