using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One file a capture names, as the capture document carries it: metadata only,
/// never the bytes (local ADR 0014 §The capture document). The bytes live in the
/// attachment store under the same <paramref name="Id"/>, uploaded through
/// <c>PUT /api/sync/attachments/{id}</c> before the capture that names them is
/// posted.
/// <para>
/// <paramref name="Id"/> is minted by the client, so a phone with no network can
/// name an attachment before it has uploaded it. <paramref name="Sha256"/> is the
/// lowercase hex digest of the bytes; the service checks it against what it
/// stored, so a capture can never name a file that is not the one uploaded.
/// </para>
/// </summary>
public sealed record AttachmentMetadata(Guid Id, string Name, string ContentType, long SizeBytes, string Sha256)
{
    /// <summary>What this build has no member for, carried through as it
    /// arrived — see <see cref="TaskPayload.Unrecognised"/>. The record travels
    /// inside the task document, so it keeps the same rule as every other part of
    /// that document.</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Unrecognised { get; init; }
}

/// <summary>
/// What the attachment store holds under one id: the type it will be served
/// with, how many bytes, and their digest. The answer to an upload, and what a
/// repeated upload is compared with. The file name is not here: it belongs to
/// the capture that names the attachment, not to the bytes.
/// </summary>
public sealed record StoredAttachment(Guid Id, string ContentType, long SizeBytes, string Sha256);
