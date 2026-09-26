namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// One file a capture names, as a channel hands it to the intake: the metadata
/// of ADR 0014's capture document, and never the bytes — those are fetched by
/// <paramref name="Id"/> through <see cref="Services.IInboxAttachmentSource"/>.
/// <paramref name="Sha256"/> is the lowercase hex digest the bytes must hash to.
/// </summary>
public sealed record InboxCaptureAttachmentDto(Guid Id, string Name, string ContentType, long SizeBytes, string Sha256);

/// <summary>
/// One file on an inbox item, as the pane sees it.
/// <para>
/// <paramref name="Downloaded"/> says the file is on this machine;
/// <paramref name="LastError"/> says why it is not, when a fetch was tried and
/// failed. Neither set is a file still waiting for its first fetch. The path is
/// not published: the pane asks the module to read or open a file by its id, so
/// where the workspace keeps it stays the module's business.
/// </para>
/// </summary>
public sealed record InboxAttachmentDto(
    Guid Id,
    string Name,
    string ContentType,
    long SizeBytes,
    bool IsImage,
    bool Downloaded,
    string? LastError);
