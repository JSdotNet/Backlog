using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Abstractions.Services;

/// <summary>
/// Where an inbox item's files live on this machine: one folder per item, under
/// the workspace's inbox folder, named by the item's id.
/// <para>
/// The item owns its local copy once the intake has written it (local ADR 0014
/// §The desktop's intake) — the blob is a courier, not a home. One folder per
/// item rather than one shared folder, because that folder is what routing hands
/// to the task as its attachment: a task points at one place, and the place is
/// the item's files and nothing else.
/// </para>
/// <para>
/// A port, answered in <c>src/Infrastructure</c>, because the module does no
/// disk IO of its own; and optional on every handler that takes it, for the
/// reason <see cref="IInboxAttachmentSource"/> is.
/// </para>
/// </summary>
public interface IInboxAttachmentFiles
{
    /// <summary>The folder an item's files are written into. Named whether or
    /// not it exists yet: routing hands it on only when the item has files.</summary>
    string FolderFor(Guid itemId);

    /// <summary>Writes one file into the item's folder under
    /// <paramref name="fileName"/> — a bare name the module has already made
    /// unique within the item — replacing whatever was there, and answers the
    /// full path written.</summary>
    Task<string> WriteAsync(Guid itemId, string fileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Where <paramref name="fileName"/> would be written for the item,
    /// so the intake can ask whether it already holds it before fetching.</summary>
    string PathFor(Guid itemId, string fileName);

    /// <summary>Whether <paramref name="path"/> exists and its bytes hash to
    /// <paramref name="sha256"/> (lowercase hex). The one question idempotency
    /// asks: a file already on disk with the right digest is not fetched again.</summary>
    Task<bool> HoldsAsync(string path, string sha256, CancellationToken cancellationToken = default);

    /// <summary>The file's bytes, or null when it is not there.</summary>
    Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Opens the file with whatever this machine opens that kind of
    /// file with.</summary>
    Result Open(string path);
}
