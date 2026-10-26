using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where an owner's notifications and the replies to them are kept while they travel between
/// that person's devices (local ADR 0039). Declared here and implemented outside, so the module
/// says what it needs of storage without naming Cosmos.
/// <para>
/// Two collections behind one port, because one rule joins them: a reply is stored only
/// against a notification this owner's replica holds, addressed to the owner instance that
/// notification names. The notification side is a relay with a change feed, like
/// <see cref="IAnnotationReplica"/>; the reply side is a mailbox the owner instance empties —
/// it lists what is addressed to it and deletes each reply once processed, so there is no
/// cursor to keep.
/// </para>
/// <para>
/// The owner of a notification is its owner instance — the publishing desktop's installation
/// identity (ADR 0039 §1) — not the sync device it paired as. The replica remembers which sync
/// device last wrote each notification, and that device speaks for the instance: it is the one
/// that may list and delete the instance's replies. A desktop that pairs again pushes its open
/// notifications under its new device and so takes its replies with it.
/// </para>
/// <para>
/// Every method takes the owner from the caller's validated token, and nothing underneath
/// re-checks it (.devbook/arc42/adr/0005 §Identity). That is what makes "a reply comes from a
/// device under the same owner" true by construction: a reply is stored in the caller's own
/// partition, against a notification found there, or not at all.
/// </para>
/// </summary>
public interface INotificationReplica
{
    /// <summary>
    /// Writes a batch of notifications. Each one is written when the replica does not hold it
    /// yet, or holds it under the same owner instance and no later than this change; the
    /// writing device is recorded as the one that speaks for that instance. A notification held
    /// under another owner instance is not written and is named in
    /// <see cref="NotificationUpsertResult.NotOwned"/>. A change older than the one held is
    /// dropped and not counted.
    /// <para>
    /// A written change that the replica did not hold yet, or that moves the state it held, is
    /// also named in <see cref="NotificationUpsertResult.Transitions"/>: those are the changes a
    /// phone is pushed (ADR 0039 §4). A re-push in the state already held — the owner's
    /// ten-minute presence refresh — is written and is no transition. A batch that faults part
    /// way reports no transitions at all, and its retry finds the written ones in the state
    /// already held: those phones are not pushed. Accepted, because a lost push costs a buzz and
    /// never data — the phone finds the change on its next pull.
    /// </para>
    /// </summary>
    Task<NotificationUpsertResult> Upsert(OwnerScope scope, IReadOnlyList<NotificationChange> changes, CancellationToken cancellationToken = default);

    /// <summary>One page of this owner's notification feed, from <paramref name="cursor"/> or
    /// from the beginning when it is null.</summary>
    Task<NotificationReplicaPage> ReadChanges(OwnerId owner, NotificationReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a reply from the caller's device. Refused as
    /// <see cref="NotificationReplyStoreOutcome.NotificationNotFound"/> when this owner's replica
    /// holds no such notification, or holds it under another owner instance than the reply
    /// names. A reply id already stored is not stored again.
    /// </summary>
    Task<NotificationReplyStoreOutcome> StoreReply(OwnerScope scope, NotificationReplyPost reply, CancellationToken cancellationToken = default);

    /// <summary>The replies addressed to <paramref name="ownerInstanceId"/>, in the order the
    /// replica took them, at most <paramref name="maxItems"/> — or none when the caller's device
    /// does not speak for that instance.</summary>
    Task<IReadOnlyList<NotificationReplyRecord>> ListReplies(OwnerScope scope, Guid ownerInstanceId, int maxItems, CancellationToken cancellationToken = default);

    /// <summary>Deletes one reply, and says whether there was one to delete. A reply addressed
    /// to an instance the caller's device does not speak for is left alone.</summary>
    Task<bool> DeleteReply(OwnerScope scope, Guid replyId, CancellationToken cancellationToken = default);
}

/// <summary>What a notification push did: how many were written, which were not because they
/// belong to another owner instance, and which of the written ones were published now or moved
/// state.</summary>
/// <param name="Accepted">How many changes were written.</param>
/// <param name="NotOwned">The ids held under another owner instance, not written.</param>
/// <param name="Transitions">The written changes that were new to the replica or moved the
/// state it held, in the order the push carried them.</param>
public sealed record NotificationUpsertResult(int Accepted, IReadOnlyList<Guid> NotOwned, IReadOnlyList<NotificationChange> Transitions);

/// <summary>What storing a reply did.</summary>
public enum NotificationReplyStoreOutcome
{
    /// <summary>Stored now.</summary>
    Stored,

    /// <summary>A reply with that id was already stored; nothing changed.</summary>
    AlreadyStored,

    /// <summary>Refused: this owner's replica holds no such notification under that owner
    /// instance.</summary>
    NotificationNotFound
}
