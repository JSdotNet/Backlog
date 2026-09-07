using System.Collections.Concurrent;
using System.Globalization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed replica replaces — the same status
/// as <see cref="InMemoryDeviceRegistry"/> beside it, and no more permanent.
/// Everything is lost on restart, which for the task replica means a device
/// pulls the owner's tasks again from the beginning.
/// <para>
/// It is what makes the sync service runnable with no emulator: a bare
/// <c>dotnet run</c> and every endpoint test get this, because the Cosmos
/// registration no-ops when there is no connection string and leaves the
/// <c>TryAdd</c> here standing.
/// </para>
/// <para>
/// Keyed by <see cref="OwnerId"/> at the outer level, exactly as the retired
/// <c>SyncStore</c> was, and that nesting is doing real work rather than
/// organising a dictionary: an id belonging to another owner is unreachable by
/// construction here instead of by a predicate somebody could forget to write.
/// The cross-owner tests are only worth running against a store that could not
/// have leaked in the first place — otherwise they assert that one <c>Where</c>
/// clause exists.
/// </para>
/// <para>
/// The change feed is a sequence number per owner, standing in for Cosmos's
/// <c>_ts</c>: monotonic, assigned on write, and the only ordering either
/// adapter promises. The continuation is that number as text, opaque to
/// everything above — the pull handler wraps whatever it gets from here in a
/// signed cursor through <c>ISyncCursorCodec</c>, so a cursor minted against
/// this adapter is checked by the same code that checks one minted against
/// Cosmos and the security tests exercise the real codec either way.
/// </para>
/// </summary>
public sealed class InMemoryTaskReplica : ITaskReplica
{
    private readonly ConcurrentDictionary<OwnerId, OwnerPartition> _byOwner = new();

    public Task<int> Upsert(
        OwnerScope scope,
        IReadOnlyList<TaskChange> changes,
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

    public Task<TaskReplicaPage> ReadChanges(
        OwnerId owner,
        TaskReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        // A cursor for another owner reaching this far is a bug in the caller,
        // not something a person did: the handler verifies it before the replica
        // is touched. It throws rather than returning an empty page, because an
        // empty page would look like a drained feed and hide the bug.
        if (cursor is { } resume && resume.Owner != owner)
        {
            throw new InvalidOperationException(
                "The cursor belongs to another owner. Verify it against the caller before it gets here.");
        }

        if (!_byOwner.TryGetValue(owner, out var partition))
        {
            // No partition and no writes, so the feed is at position zero and
            // stays there. A cursor still comes back, for the same reason it
            // does on a drained feed.
            return Task.FromResult(new TaskReplicaPage(
                [], new TaskReplicaCursor(owner, ContinuationFrom(0)), HasMore: false));
        }

        return Task.FromResult(partition.Read(owner, PositionOf(cursor), maxItems));
    }

    public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(
        OwnerId owner,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_byOwner.TryGetValue(owner, out var partition) ? partition.Captures() : []);

    public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byOwner.TryGetValue(owner, out var partition) ? partition.Find(id) : null);

    private static string ContinuationFrom(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>An unparsable continuation reads as the beginning rather than
    /// throwing. It cannot happen — the codec only ever hands back something
    /// this adapter minted — and "from the start" is the harmless reading of an
    /// impossible input.</summary>
    private static long PositionOf(TaskReplicaCursor? cursor) =>
        cursor is { } resume && long.TryParse(resume.Continuation, CultureInfo.InvariantCulture, out var position)
            ? position
            : 0;

    /// <summary>One owner's documents and their feed. The lock covers the
    /// sequence number and the write together: two devices pushing at once must
    /// not be handed the same position, or a pull would skip one of them.</summary>
    private sealed class OwnerPartition
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<Guid, StoredDocument> _documents = [];
        private long _sequence;

        internal void Write(TaskChange change, Guid deviceId)
        {
            lock (_gate)
            {
                // Whole-document last-write-wins: the arriving version replaces
                // whatever was there, and re-writing a document moves it to the
                // end of the feed exactly as a Cosmos upsert restamps `_ts`.
                _documents[change.Id] = new StoredDocument(change, deviceId, ++_sequence);
            }
        }

        internal TaskReplicaPage Read(OwnerId owner, long after, int maxItems)
        {
            lock (_gate)
            {
                var page = _documents.Values
                    .Where(document => document.Sequence > after)
                    .OrderBy(document => document.Sequence)
                    .Take(maxItems)
                    .ToList();

                // The feed advances even when the page is empty, so a client
                // polling a quiet replica does not rescan from its old position
                // every time.
                var position = page.Count > 0 ? page[^1].Sequence : _sequence;

                // A full page, which here really does mean there may be more:
                // the take is exact, so this never claims a drained feed while
                // documents are left. The worst it does is say "more" when the
                // next page turns out to be empty, which the contract allows and
                // the Cosmos adapter does on every pull.
                return new TaskReplicaPage(
                    [.. page.Select(document => document.ToRecord())],
                    new TaskReplicaCursor(owner, ContinuationFrom(position)),
                    page.Count == maxItems);
            }
        }

        internal IReadOnlyList<TaskChangeRecord> Captures()
        {
            lock (_gate)
            {
                return
                [
                    .. _documents.Values
                        .Where(document => document.Change is { DeletedAt: null, Task.SourceInboxId: not null })
                        .OrderByDescending(document => document.Change.Task.CreatedAt)
                        .Select(document => document.ToRecord()),
                ];
            }
        }

        internal TaskChangeRecord? Find(Guid id)
        {
            lock (_gate)
            {
                return _documents.TryGetValue(id, out var document) ? document.ToRecord() : null;
            }
        }
    }

    /// <summary>A document as stored: the change, who wrote it, and where it
    /// sits in the feed.</summary>
    private sealed record StoredDocument(TaskChange Change, Guid DeviceId, long Sequence)
    {
        internal TaskChangeRecord ToRecord() => new(Change, DeviceId, Sequence);
    }
}
