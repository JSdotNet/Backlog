using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Ports;

/// <summary>
/// Where the bytes of an owner's attachments wait between the device that
/// uploaded them and the desktop that fetches them (local ADR 0014). Declared
/// here and implemented outside, beside <see cref="ITaskReplica"/>, so the
/// module says what it needs of storage without naming Blob Storage.
/// <para>
/// A courier, not a home: the desktop keeps its own copy once it has fetched
/// one, the acknowledgement tombstone releases the blobs, and a 30-day
/// lifecycle rule on the deployed container removes whatever that missed.
/// </para>
/// <para>
/// Every method takes the owner, and the owner comes from the caller's
/// validated token. The store keys each attachment under that owner — deployed,
/// the blob name is <c>{ownerId}/{attachmentId}</c> — so an id belonging to
/// somebody else is a key under a prefix this caller never reaches, and is
/// indistinguishable from one that was never uploaded. As with the replica, the
/// service reaches storage under one identity that can see every owner, so
/// these parameters are the boundary.
/// </para>
/// <para>
/// A store that cannot be reached throws <see cref="SyncReplicaException"/>
/// with <c>SyncErrorCodes.AttachmentStoreUnavailable</c>, which the host
/// renders as a 503 in the one place it renders the replica's.
/// </para>
/// </summary>
public interface IAttachmentStore
{
    /// <summary>What is stored under this owner and id, or null when nothing
    /// is. Reads the metadata only, never the bytes.</summary>
    Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <paramref name="content"/> to its end into the store without
    /// making it visible, and hands back what can commit it.
    /// <para>
    /// Staged rather than written because the digest is only known once the
    /// last byte has been read: the caller checks it, and commits only bytes
    /// that match what the upload declared. Bytes that are never committed are
    /// never served — deployed they are uncommitted blocks, which Blob Storage
    /// discards on its own after a week — so a refused upload leaves nothing a
    /// <see cref="Find"/> or an <see cref="Open"/> could see.
    /// </para>
    /// <para>
    /// Exceptions thrown by <paramref name="content"/> propagate unchanged, so a
    /// caller that bounds the stream sees its own refusal rather than a store
    /// failure.
    /// </para>
    /// </summary>
    Task<IStagedAttachment> Stage(OwnerId owner, Guid id, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The stored bytes and their metadata, or null when nothing is
    /// stored under this owner and id. The caller owns the stream and disposes
    /// it.</summary>
    Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes the attachment if it is there. Deleting one that is not
    /// is not an error: a release that ran twice, or ran after the lifecycle rule,
    /// has nothing left to do.</summary>
    Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Bytes read into the store and not yet visible. Committing makes
/// them the attachment, with the metadata the caller has checked; dropping it
/// without committing leaves nothing behind that anything reads.</summary>
public interface IStagedAttachment
{
    /// <summary>Makes the staged bytes the attachment, replacing whatever the id
    /// held, served from then on with <paramref name="attachment"/>'s content
    /// type and digest.</summary>
    Task Commit(StoredAttachment attachment, CancellationToken cancellationToken = default);
}

/// <summary>An attachment being read back: what it is, and its bytes.</summary>
public sealed record AttachmentContent(StoredAttachment Attachment, Stream Content);
