using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's session records are kept while they travel between that
/// person's machines. Declared here and implemented outside, so the module says
/// what it needs of storage without naming Cosmos.
/// <para>
/// <strong>The surface is deliberately this small — smaller than
/// <see cref="ITaskReplica"/>'s, which is itself deliberately small.</strong>
/// There is no <c>Find</c> and no equivalent of <c>ListCaptures</c>, because
/// nothing queries session records: the fleet view is answered from the reading
/// device's own session log, and .arc42/adr/0005 §Storage asks that no query
/// serve a UI out of the replica. The moment a screen could be answered from
/// here, the replica and the log that owns the evidence would start to disagree
/// about which one is true — and for sessions that is worse than it is for
/// tasks, because a session's state is derived from evidence rather than
/// asserted, so a second opinion is not a stale copy but a fabricated one.
/// </para>
/// <para>
/// Two operations, and neither of them is an update. Session records are
/// single-writer and append-only (.arc42/adr/0005 §Session records): a session
/// that moves gets a later record rather than an edit to an earlier one, there
/// is no tombstone, there is no <c>updated_at</c>, and there is no conflict to
/// resolve because no second machine has anything to say about a session it did
/// not run.
/// </para>
/// <para>
/// Every method takes the owner, and the owner comes from the caller's
/// validated token. Nothing underneath re-checks it: the service reaches its
/// store under one identity that can see every partition, so these parameters
/// are the boundary (.arc42/adr/0005 §Identity).
/// </para>
/// </summary>
public interface ISessionReplica
{
    /// <summary>
    /// Appends a batch of this machine's records and answers how many were
    /// taken.
    /// <para>
    /// The machine id is the scope's and never the batch's — no
    /// <see cref="SessionRecord"/> carries one — so an implementation cannot
    /// write a record attributed to a machine other than the caller even if it
    /// tried. That is §Session records' "a caller may only write records stamped
    /// with its own machine id", held by construction.
    /// </para>
    /// <para>
    /// Append-only in the sense that record means: re-pushing a session replaces
    /// that machine's own record for it and moves it to the end of the feed,
    /// which is how a still-running session reports later evidence. It never
    /// touches another machine's record, and nothing here ever deletes one —
    /// the container's twelve-month TTL is what removes a record, and nothing
    /// else does.
    /// </para>
    /// </summary>
    Task<int> Append(OwnerScope scope, IReadOnlyList<SessionRecord> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of this owner's session feed, from <paramref name="cursor"/> or
    /// from the beginning when it is null. Beginning rather than now: a newly
    /// paired device has to receive the session history the owner already has,
    /// and there is no separate bootstrap path in the model.
    /// </summary>
    Task<SessionReplicaPage> ReadChanges(
        OwnerId owner,
        SessionReplicaCursor? cursor,
        int maxItems,
        CancellationToken cancellationToken = default);
}
