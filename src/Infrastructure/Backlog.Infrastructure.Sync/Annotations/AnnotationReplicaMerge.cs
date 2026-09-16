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
/// version wins when it is newer, or when the local copy is older and has
/// already been pushed; a local edit still waiting behind the push watermark
/// is kept, because it will win on the replica when it gets there. A
/// tombstone is a version like any other.
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
    /// and answers how many documents it wrote. <paramref name="pushWatermark"/>
    /// is how far this device has had its own writes accepted; it is passed in
    /// rather than read from a state store so the rule can be exercised without
    /// one.
    /// </summary>
    public AnnotationMergeOutcome Apply(IReadOnlyList<AnnotationChangeRecord> page, DateTimeOffset pushWatermark)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;

        foreach (var record in Winners(page))
        {
            var local = _store.Find(record.Change.Id);

            if (!ShouldApply(record.Change, local, pushWatermark)) continue;

            _store.Apply(ToAnnotation(record.Change));
            applied++;
        }

        return new AnnotationMergeOutcome(applied);
    }

    /// <summary>The apply-against-local decision on its own.</summary>
    internal static bool ShouldApply(AnnotationChange inbound, DevbookAnnotation? local, DateTimeOffset pushWatermark)
    {
        if (local is null) return true;

        // The same document coming back round: both stamps, because a tombstone
        // and the live remark it replaced can share an UpdatedAt only if one of
        // them never happened.
        if (inbound.UpdatedAt == local.UpdatedAt && inbound.DeletedAt == local.DeletedAt) return false;

        if (inbound.UpdatedAt > local.UpdatedAt) return true;

        // Older than the local copy, so it may only overwrite one this device
        // has already sent. Everything above the watermark is due to be pushed
        // and will win there.
        return local.UpdatedAt <= pushWatermark;
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
