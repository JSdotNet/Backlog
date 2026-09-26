using System.Collections.Concurrent;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// In-memory stand-in that the blob-backed store replaces — the same status as
/// <see cref="InMemoryTaskReplica"/> beside it. Everything is lost on restart,
/// which for an attachment means a capture naming it arrives on the desktop
/// without its file, the same thing the deployed lifecycle rule does to one left
/// waiting too long.
/// <para>
/// It is what lets a bare <c>dotnet run</c> of the service and every endpoint
/// test upload and download with no Azurite: the blob registration no-ops in
/// Development when there is no connection string, and the <c>TryAdd</c> in
/// <c>AddSyncModule</c> leaves this standing.
/// </para>
/// <para>
/// Keyed by owner and id together, which is the whole of the owner scope here:
/// a lookup never names another owner's key because the owner is part of it.
/// </para>
/// </summary>
public sealed class InMemoryAttachmentStore : IAttachmentStore
{
    private readonly ConcurrentDictionary<(OwnerId Owner, Guid Id), (StoredAttachment Attachment, byte[] Bytes)> _blobs = new();

    public Task<StoredAttachment?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.TryGetValue((owner, id), out var blob) ? blob.Attachment : null);

    public async Task<IStagedAttachment> Stage(
        OwnerId owner,
        Guid id,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        return new Staged(this, owner, id, buffer.ToArray());
    }

    public Task<AttachmentContent?> Open(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.TryGetValue((owner, id), out var blob)
            ? new AttachmentContent(blob.Attachment, new MemoryStream(blob.Bytes, writable: false))
            : null);

    public Task Delete(OwnerId owner, Guid id, CancellationToken cancellationToken = default)
    {
        _blobs.TryRemove((owner, id), out _);
        return Task.CompletedTask;
    }

    private sealed class Staged(InMemoryAttachmentStore store, OwnerId owner, Guid id, byte[] bytes) : IStagedAttachment
    {
        public Task Commit(StoredAttachment attachment, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(attachment);

            store._blobs[(owner, id)] = (attachment, bytes);
            return Task.CompletedTask;
        }
    }
}
