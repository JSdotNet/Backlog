using System.Collections.Concurrent;
using System.Globalization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed annotation replica replaces — the
/// same status as <see cref="InMemoryTaskReplica"/>, and the same shape: one
/// partition per owner so another owner's document is unreachable by
/// construction, and a per-owner sequence number standing in for Cosmos's
/// <c>_ts</c> as the only ordering either adapter promises. Everything is lost on
/// restart, which means a device pulls the owner's annotations again from the
/// beginning.
/// </summary>
public sealed class InMemoryAnnotationReplica : IAnnotationReplica
{
    private readonly ConcurrentDictionary<OwnerId, OwnerPartition> _byOwner = new();

    public Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<AnnotationChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var partition = _byOwner.GetOrAdd(scope.OwnerId, _ => new OwnerPartition());

        foreach (var change in changes)
        {
            partition.Write(change, scope.DeviceId.Value);
        }

        return Task.FromResult(changes.Count);
    }

    public Task<AnnotationReplicaPage> ReadChanges(
        OwnerId owner,
        AnnotationReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        // A cursor for another owner reaching this far is a bug in the caller:
        // the handler verifies it before the replica is touched. It throws rather
        // than returning an empty page, because an empty page would look like a
        // drained feed and hide the bug.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        if (!_byOwner.TryGetValue(owner, out var partition))
        {
            return Task.FromResult(new AnnotationReplicaPage(
                [], new AnnotationReplicaCursor(owner, ContinuationFrom(0)), HasMore: false));
        }

        return Task.FromResult(partition.Read(owner, PositionOf(cursor), maxItems));
    }

    private static string ContinuationFrom(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>An unparsable continuation reads as the beginning rather than
    /// throwing, for the reason <see cref="InMemoryTaskReplica"/> gives: it
    /// cannot happen, and "from the start" is the harmless reading.</summary>
    private static long PositionOf(AnnotationReplicaCursor? cursor) =>
        cursor is { } resume && long.TryParse(resume.Continuation, CultureInfo.InvariantCulture, out var position)
            ? position
            : 0;

    /// <summary>One owner's documents and their feed. The lock covers the
    /// sequence number and the write together, so two devices pushing at once
    /// are never handed the same position.</summary>
    private sealed class OwnerPartition
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, StoredDocument> _documents = [];
        private long _sequence;

        internal void Write(AnnotationChange change, Guid deviceId)
        {
            lock (_gate)
            {
                // Whole-document last-write-wins: re-writing a document moves it
                // to the end of the feed, exactly as a Cosmos upsert restamps `_ts`.
                _documents[change.Id] = new StoredDocument(change, deviceId, ++_sequence);
            }
        }

        internal AnnotationReplicaPage Read(OwnerId owner, long after, int maxItems)
        {
            lock (_gate)
            {
                var page = _documents.Values
                    .Where(document => document.Sequence > after)
                    .OrderBy(document => document.Sequence)
                    .Take(maxItems)
                    .ToList();

                // The feed advances even when the page is empty, so a client
                // polling a quiet replica does not rescan from its old position.
                var position = page.Count > 0 ? page[^1].Sequence : _sequence;

                return new AnnotationReplicaPage(
                    [.. page.Select(document => document.ToRecord())],
                    new AnnotationReplicaCursor(owner, ContinuationFrom(position)),
                    page.Count == maxItems);
            }
        }
    }

    private sealed record StoredDocument(AnnotationChange Change, Guid DeviceId, long Sequence)
    {
        internal AnnotationChangeRecord ToRecord() => new(Change, DeviceId, Sequence);
    }
}
