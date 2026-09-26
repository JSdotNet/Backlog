namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// Capture pushed from a device into the sync layer.
/// <para>
/// <paramref name="Title"/> and <paramref name="Source"/> are required; the rest
/// are optional and default to null, so a client that sends only the first two
/// — the desktop pane, the editor extension, an older phone — is answered
/// exactly as it always was. Every field is bounded, and the bounds live at the
/// edge that accepts one rather than here — see <c>InboxEndpoints</c>. A capture
/// becomes a task document, so an unbounded field is a document Cosmos refuses
/// with an error the person can do nothing about, and an empty title is a task
/// nobody can act on.
/// </para>
/// <para>
/// <paramref name="Id"/> is the client's own id for the capture. Minted before
/// the first attempt and sent on every retry, it is what makes a retry after a
/// timed-out 201 cost nothing: the service answers the stored capture with 200
/// instead of writing a second one. Without it the service mints the id, as it
/// always has.
/// </para>
/// <para>
/// <paramref name="Tags"/> are names, with or without the <c>#</c>; the desktop
/// stores them bare. <paramref name="Person"/> is who the thought is about or
/// from, with or without the <c>@</c> — and it is the only place a person goes:
/// a tag that reads as one is refused, because a person is never a tag.
/// </para>
/// <para>
/// <paramref name="Attachments"/> names files already uploaded through
/// <c>PUT /api/sync/attachments/{id}</c> (local ADR 0014). The service refuses a
/// capture naming one this owner has not uploaded, or one whose size or digest
/// differs from what was stored, so a capture on the replica never names a blob
/// that is not there.
/// </para>
/// </summary>
public sealed record CaptureRequest(
    string Title,
    string Source,
    Guid? Id = null,
    string? BodyMd = null,
    IReadOnlyList<string>? Tags = null,
    string? Person = null,
    IReadOnlyList<AttachmentMetadata>? Attachments = null);

/// <summary>
/// An unsynced capture awaiting pickup by the desktop.
/// <para>
/// The record carries no owner id, and that is deliberate: the owner is never
/// something a caller states, it is something the service reads out of the
/// caller's token. See <see cref="SyncClaims"/>.
/// </para>
/// <para>
/// <paramref name="BodyMd"/>, <paramref name="Tags"/> and <paramref name="Person"/>
/// came later and are optional, so a reader that knows only the first four —
/// the desktop pane, the editor extension — reads the same record it always
/// did. The person is split back out of the <c>@name</c> tag it travels as, so
/// a reader never has to know that convention to show who a capture is about.
/// <paramref name="Attachments"/> is the metadata of the files the capture
/// names; the bytes are fetched one by one from the attachment store.
/// </para>
/// </summary>
public sealed record InboxItem(
    Guid Id,
    string Title,
    string Source,
    DateTimeOffset CapturedAt,
    string? BodyMd = null,
    IReadOnlyList<string>? Tags = null,
    string? Person = null,
    IReadOnlyList<AttachmentMetadata>? Attachments = null);
