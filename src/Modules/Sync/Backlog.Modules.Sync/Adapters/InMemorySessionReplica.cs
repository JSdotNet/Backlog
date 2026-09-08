using System.Collections.Concurrent;
using System.Globalization;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the Cosmos-backed session replica replaces — the same
/// status as <see cref="InMemoryTaskReplica"/> beside it, and no more permanent.
/// Everything is lost on restart, which for the session replica means a device
/// pulls the owner's session history again from the beginning.
/// <para>
/// It is what makes the sync service runnable with no emulator: a bare
/// <c>dotnet run</c> and every endpoint test get this, because the Cosmos
/// registration no-ops when there is no connection string and leaves the
/// <c>TryAdd</c> here standing.
/// </para>
/// <para>
/// Keyed by <see cref="OwnerId"/> at the outer level, and that nesting is doing
/// real work rather than organising a dictionary: a record belonging to another
/// owner is unreachable by construction here instead of by a predicate somebody
/// could forget to write. The cross-owner tests .arc42/adr/0005 §Consequences
/// asks for are only worth running against a store that could not have leaked in
/// the first place — otherwise they assert that one <c>Where</c> clause exists.
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
public sealed class InMemorySessionReplica : ISessionReplica
{
    private readonly ConcurrentDictionary<OwnerId, OwnerPartition> _byOwner = new();

    public Task<int> Append(
        OwnerScope scope,
        IReadOnlyList<SessionRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var partition = _byOwner.GetOrAdd(scope.OwnerId, _ => new OwnerPartition());

        foreach (var record in records)
        {
            partition.Write(record, scope.DeviceId.Value);
        }

        return Task.FromResult(records.Count);
    }

    public Task<SessionReplicaPage> ReadChanges(
        OwnerId owner,
        SessionReplicaCursor? cursor,
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
            return Task.FromResult(new SessionReplicaPage(
                [], new SessionReplicaCursor(owner, ContinuationFrom(0)), HasMore: false));
        }

        return Task.FromResult(partition.Read(owner, PositionOf(cursor), maxItems));
    }

    private static string ContinuationFrom(long sequence) => sequence.ToString(CultureInfo.InvariantCulture);

    /// <summary>An unparsable continuation reads as the beginning rather than
    /// throwing. It cannot happen — the codec only ever hands back something
    /// this adapter minted — and "from the start" is the harmless reading of an
    /// impossible input.</summary>
    private static long PositionOf(SessionReplicaCursor? cursor) =>
        cursor is { } resume && long.TryParse(resume.Continuation, CultureInfo.InvariantCulture, out var position)
            ? position
            : 0;

    /// <summary>One owner's records and their feed. The lock covers the sequence
    /// number and the write together: two machines pushing at once must not be
    /// handed the same position, or a pull would skip one of them.</summary>
    private sealed class OwnerPartition
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, StoredRecord> _records = new(StringComparer.Ordinal);
        private long _sequence;

        internal void Write(SessionRecord record, Guid machineId)
        {
            lock (_gate)
            {
                // Keyed the same way the Cosmos document is, and the machine id
                // leading the key is what makes .arc42/adr/0005's single-writer
                // rule structural: this machine can only ever address records
                // whose key begins with its own id, so re-pushing a session
                // replaces its own record and can never touch another machine's
                // copy of the same session id.
                //
                // Replacing moves the record to the end of the feed, which is
                // what append-only means here: a still-running session reports
                // later evidence as a later record rather than as an edit, and
                // there is no tombstone and no updated_at to reconcile.
                _records[SessionRecordKey.For(machineId, record)] =
                    new StoredRecord(record, machineId, ++_sequence);
            }
        }

        internal SessionReplicaPage Read(OwnerId owner, long after, int maxItems)
        {
            lock (_gate)
            {
                var page = _records.Values
                    .Where(stored => stored.Sequence > after)
                    .OrderBy(stored => stored.Sequence)
                    .Take(maxItems)
                    .ToList();

                // The feed advances even when the page is empty, so a client
                // polling a quiet replica does not rescan from its old position
                // every time. A fleet nobody is working on is quiet for hours, so
                // that is the ordinary case here rather than the edge one.
                var position = page.Count > 0 ? page[^1].Sequence : _sequence;

                // A full page, which here really does mean there may be more: the
                // take is exact, so this never claims a drained feed while records
                // are left. The worst it does is say "more" when the next page
                // turns out to be empty, which the contract allows and the Cosmos
                // adapter does on every pull.
                return new SessionReplicaPage(
                    [.. page.Select(stored => stored.ToEntry())],
                    new SessionReplicaCursor(owner, ContinuationFrom(position)),
                    page.Count == maxItems);
            }
        }
    }

    /// <summary>A record as stored: the record, which machine wrote it, and where
    /// it sits in the feed.</summary>
    private sealed record StoredRecord(SessionRecord Record, Guid MachineId, long Sequence)
    {
        internal SessionRecordEntry ToEntry() => new(Record, MachineId, Sequence);
    }
}
