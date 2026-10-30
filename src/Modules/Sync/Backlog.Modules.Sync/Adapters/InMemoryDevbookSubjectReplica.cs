using System.Collections.Concurrent;
using System.Globalization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed subject state replica replaces —
/// <see cref="InMemoryAnnotationReplica"/>'s shape: one partition per owner so
/// another owner's document is unreachable by construction, and a per-owner
/// sequence number standing in for Cosmos's <c>_ts</c>. Everything is lost on
/// restart, which means a device pulls the owner's subject states again from the
/// beginning.
/// </summary>
public sealed class InMemoryDevbookSubjectReplica : IDevbookSubjectReplica
{
    private readonly ConcurrentDictionary<OwnerId, OwnerPartition> _byOwner = new();

    public Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<DevbookSubjectChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var partition = _byOwner.GetOrAdd(scope.OwnerId, _ => new OwnerPartition());
        var accepted = 0;

        foreach (var change in changes)
        {
            if (partition.Write(change, scope.DeviceId.Value)) accepted++;
        }

        return Task.FromResult(accepted);
    }

    public Task<DevbookSubjectReplicaPage> ReadChanges(
        OwnerId owner,
        DevbookSubjectReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        // A cursor for another owner reaching this far is a bug in the caller;
        // an empty page would look like a drained feed and hide it.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        if (!_byOwner.TryGetValue(owner, out var partition))
        {
            return Task.FromResult(new DevbookSubjectReplicaPage(
                [], new DevbookSubjectReplicaCursor(owner, ContinuationFrom(0)), HasMore: false));
        }

        return Task.FromResult(partition.Read(owner, PositionOf(cursor), maxItems));
    }

    private static string ContinuationFrom(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>An unparsable continuation reads as the beginning, for the
    /// reason <see cref="InMemoryTaskReplica"/> gives.</summary>
    private static long PositionOf(DevbookSubjectReplicaCursor? cursor) =>
        cursor is { } resume && long.TryParse(resume.Continuation, CultureInfo.InvariantCulture, out var position)
            ? position
            : 0;

    /// <summary>One owner's documents and their feed, under one lock so two
    /// devices pushing at once are never handed the same position.</summary>
    private sealed class OwnerPartition
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, StoredDocument> _documents = [];
        private long _sequence;

        /// <summary>Writes the change unless the document held is already at
        /// least this version, and says whether it did. A stale copy does not
        /// move in the feed, so a device that was caught up is not handed its
        /// own echo as a change.</summary>
        internal bool Write(DevbookSubjectChange change, Guid deviceId)
        {
            lock (_gate)
            {
                if (_documents.TryGetValue(change.Id, out var held)
                    && !DevbookSubjectChangePrecedence.Supersedes(change, held.Change))
                {
                    return false;
                }

                _documents[change.Id] = new StoredDocument(change, deviceId, ++_sequence);
                return true;
            }
        }

        internal DevbookSubjectReplicaPage Read(OwnerId owner, long after, int maxItems)
        {
            lock (_gate)
            {
                var page = _documents.Values
                    .Where(document => document.Sequence > after)
                    .OrderBy(document => document.Sequence)
                    .Take(maxItems)
                    .ToList();

                var position = page.Count > 0 ? page[^1].Sequence : _sequence;

                return new DevbookSubjectReplicaPage(
                    [.. page.Select(document => document.ToRecord())],
                    new DevbookSubjectReplicaCursor(owner, ContinuationFrom(position)),
                    page.Count == maxItems);
            }
        }
    }

    private sealed record StoredDocument(DevbookSubjectChange Change, Guid DeviceId, long Sequence)
    {
        internal DevbookSubjectChangeRecord ToRecord() => new(Change, DeviceId, Sequence);
    }
}
