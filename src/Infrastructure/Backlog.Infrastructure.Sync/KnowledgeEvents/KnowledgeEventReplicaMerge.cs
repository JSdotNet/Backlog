using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>What one page did: how many events and series were written.</summary>
public sealed record KnowledgeEventMergeOutcome(int Applied);

/// <summary>
/// Reconciles a page of the owner's knowledge event feed with this device's events and series —
/// <see cref="DevbookSubjects.DevbookSubjectReplicaMerge"/>'s rules over Events' replication port.
/// <para>
/// Within a page, the later of two records for one id is decided by the replica's stamp, then
/// the writing device's <c>UpdatedAt</c>, then the device id. Against the local copy, Events
/// decides (<see cref="IKnowledgeEventReplication"/>): a copy wins only when its stamp is later,
/// and then whole, at that stamp, in the same step as the write.
/// </para>
/// <para>
/// A record this device cannot file is skipped and named rather than thrown on: one whose
/// document does not read as the event or series its envelope names, or that Events finds
/// unreadable. A kind this build does not know is skipped the same way.
/// </para>
/// </summary>
public sealed class KnowledgeEventReplicaMerge
{
    private readonly IKnowledgeEventReplication _replication;
    private readonly ILogger _log;

    public KnowledgeEventReplicaMerge(IKnowledgeEventReplication replication, ILogger<KnowledgeEventReplicaMerge>? log = null)
    {
        ArgumentNullException.ThrowIfNull(replication);

        _replication = replication;
        _log = log ?? NullLogger<KnowledgeEventReplicaMerge>.Instance;
    }

    /// <summary>Whether <paramref name="inbound"/> is the later of two records for the same id —
    /// the three-field rule, static and pure so it can be asserted on its own.</summary>
    public static bool Wins(KnowledgeEventChangeRecord inbound, KnowledgeEventChangeRecord against)
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

    /// <summary>Applies a page of the feed, reduced to one winner per id first, and answers how
    /// many documents it wrote.</summary>
    public async Task<KnowledgeEventMergeOutcome> ApplyAsync(
        IReadOnlyList<KnowledgeEventChangeRecord> page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var applied = 0;

        foreach (var record in Winners(page))
        {
            var outcome = await ApplyAsync(record.Change, cancellationToken).ConfigureAwait(false);

            if (outcome == KnowledgeEventReplicaOutcome.Taken)
            {
                applied++;
            }
            else if (outcome == KnowledgeEventReplicaOutcome.Unreadable)
            {
                _log.LogWarning(
                    "Skipping {Kind} {Id} from the sync service: this device could not read it.",
                    record.Change.Kind,
                    record.Change.Id);
            }
        }

        return new KnowledgeEventMergeOutcome(applied);
    }

    private async Task<KnowledgeEventReplicaOutcome> ApplyAsync(KnowledgeEventChange change, CancellationToken cancellationToken)
    {
        if (string.Equals(change.Kind, KnowledgeEventKinds.KnowledgeEvent, StringComparison.Ordinal))
        {
            return KnowledgeEventDocuments.ReadEvent(change) is { } knowledgeEvent
                ? await _replication.ApplyEventAsync(knowledgeEvent, cancellationToken).ConfigureAwait(false)
                : KnowledgeEventReplicaOutcome.Unreadable;
        }

        if (string.Equals(change.Kind, KnowledgeEventKinds.EventSeries, StringComparison.Ordinal))
        {
            return KnowledgeEventDocuments.ReadSeries(change) is { } series
                ? await _replication.ApplySeriesAsync(series, cancellationToken).ConfigureAwait(false)
                : KnowledgeEventReplicaOutcome.Unreadable;
        }

        return KnowledgeEventReplicaOutcome.Unreadable;
    }

    private static IEnumerable<KnowledgeEventChangeRecord> Winners(IReadOnlyList<KnowledgeEventChangeRecord> page)
    {
        var winners = new Dictionary<Guid, KnowledgeEventChangeRecord>();

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
