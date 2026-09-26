using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features.StoreAttachment;

/// <summary>A device uploads one file ahead of the capture that will name it.
/// The endpoint has already checked the declared type against the allowlist,
/// the declared length against the cap, and the digest's shape; what is left is
/// the part only reading the bytes can settle.</summary>
/// <param name="Sha256">The digest the upload declared, lowercase hex.</param>
/// <param name="MaxBytes">The per-file cap, so a body with no length, or one
/// that lied about it, is stopped where it passes the cap.</param>
public sealed record StoreAttachmentCommand(
    OwnerScope Scope,
    Guid Id,
    string ContentType,
    string Sha256,
    Stream Content,
    long MaxBytes);

/// <summary>What is now stored, and whether this call is what stored it.
/// <see cref="Created"/> is false for a repeat of an upload already held — the
/// endpoint answers that 200 rather than 201.</summary>
public sealed record StoreAttachmentOutcome(StoredAttachment Attachment, bool Created);

/// <summary>
/// Streams the bytes into the store, and makes them the attachment only once
/// they are known to be what the upload said they were (local ADR 0014).
/// <para>
/// <b>A repeat is answered before a byte is read.</b> A phone that timed out
/// waiting for its 201 sends the same file again under the same id; the digest
/// it declares is enough to tell a repeat from a different file, so the stored
/// one is answered as it stands and nothing is written. A different digest under
/// a used id is a conflict rather than an overwrite, because a capture may
/// already name the first file.
/// </para>
/// <para>
/// <b>Bytes are staged, then committed.</b> The digest is only known once the
/// last byte is in, so the store holds them unseen until the size and hash have
/// been checked. An upload that runs past the cap or does not hash to its
/// declared digest is dropped uncommitted, and leaves nothing a download could
/// return.
/// </para>
/// </summary>
public sealed class StoreAttachmentCommandHandler(IAttachmentStore store)
    : ICommandHandler<StoreAttachmentCommand, Result<StoreAttachmentOutcome>>
{
    public async Task<Result<StoreAttachmentOutcome>> Handle(
        StoreAttachmentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var owner = command.Scope.OwnerId;

        if (await store.Find(owner, command.Id, cancellationToken) is { } existing)
        {
            return string.Equals(existing.Sha256, command.Sha256, StringComparison.Ordinal)
                ? new StoreAttachmentOutcome(existing, Created: false)
                : Result.Failure<StoreAttachmentOutcome>(Error.Conflict(
                    SyncErrorCodes.AttachmentConflict,
                    "That id already holds a different file. Upload a new file under a new id."));
        }

        await using var digest = new DigestingStream(command.Content, command.MaxBytes);

        IStagedAttachment staged;
        try
        {
            staged = await store.Stage(owner, command.Id, digest, cancellationToken);
        }
        catch (AttachmentTooLargeException tooLarge)
        {
            return Result.Failure<StoreAttachmentOutcome>(TooLarge(tooLarge.MaxBytes));
        }

        var actual = digest.Sha256Hex();

        if (!string.Equals(actual, command.Sha256, StringComparison.Ordinal))
        {
            return Result.Failure<StoreAttachmentOutcome>(Error.Validation(
                SyncErrorCodes.AttachmentInvalid,
                $"The body does not hash to the {SyncRoutes.AttachmentSha256Header} the upload declared; nothing was stored."));
        }

        var stored = new StoredAttachment(command.Id, command.ContentType, digest.BytesRead, actual);
        await staged.Commit(stored, cancellationToken);

        return new StoreAttachmentOutcome(stored, Created: true);
    }

    /// <summary>The refusal for an upload over the cap, worded the same whether
    /// the host saw the length up front or the stream ran past it.</summary>
    public static Error TooLarge(long maxBytes) => new(
        SyncErrorCodes.AttachmentTooLarge,
        $"An attachment's Content-Length may be at most {maxBytes} bytes; this upload was larger, and nothing was stored.",
        ErrorType.Validation);
}
