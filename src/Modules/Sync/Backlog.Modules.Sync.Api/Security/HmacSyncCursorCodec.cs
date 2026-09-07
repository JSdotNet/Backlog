using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Signs the pull cursor with HMAC-SHA256, so a client can hold it and cannot
/// change it.
/// <para>
/// The shape is <c>v1.&lt;base64url payload&gt;.&lt;base64url signature&gt;</c>.
/// Base64url rather than base64 because the cursor travels in a query string,
/// and a version prefix because the payload will eventually gain a field and a
/// cursor already in a client's hands has to be recognisable as the old shape
/// rather than as junk.
/// </para>
/// <para>
/// Registered by the host beside <see cref="JwtDeviceTokenIssuer"/>, and for the
/// same reason: the key that mints is the key that verifies, and splitting them
/// across two assemblies is how they come to disagree. The key here is derived —
/// <c>HMACSHA256(SyncTokenOptions.SigningKey, "backlog.sync.cursor.v1")</c> —
/// rather than configured separately. One secret to provision, and domain
/// separation instead of a second one: a cursor can never be mistaken for a
/// device token and neither signature is valid under the other's key, which is
/// what a second secret would have bought at the price of a deployment step
/// somebody would eventually skip.
/// </para>
/// </summary>
internal sealed class HmacSyncCursorCodec(IOptions<SyncTokenOptions> options) : ISyncCursorCodec
{
    /// <summary>The only version this codec mints, and the only one it reads.
    /// A cursor from a future version is refused as malformed rather than
    /// guessed at.</summary>
    private const string Version = "v1";

    /// <summary>The domain separation label. It is what makes the cursor key a
    /// different key from the token signing key, so changing it invalidates
    /// every cursor in flight and nothing else.</summary>
    private static readonly byte[] Purpose = "backlog.sync.cursor.v1"u8.ToArray();

    private static readonly JsonSerializerOptions PayloadFormat = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly byte[] _key = HMACSHA256.HashData(options.Value.DecodeSigningKey(), Purpose);

    public string Mint(OwnerId owner, string continuation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(owner.ToString(), continuation, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            PayloadFormat);

        return string.Concat(
            Version,
            ".",
            Base64Url.EncodeToString(payload),
            ".",
            Base64Url.EncodeToString(HMACSHA256.HashData(_key, payload)));
    }

    public Result<TaskReplicaCursor> Verify(string cursor, OwnerId caller)
    {
        if (!TryRead(cursor, out var payload))
        {
            return Malformed;
        }

        // Only after the signature checks out, and only ever against the caller
        // the token named: an owner id out of the cursor is data the client
        // handed us, and it decides nothing on its own.
        return payload.Owner == caller.ToString()
            ? new TaskReplicaCursor(caller, payload.Continuation)
            : NotYours;
    }

    private static Error Malformed => Error.Validation(
        SyncErrorCodes.SyncCursorMalformed,
        "That is not a cursor this service issued. Pull again without one to start from the beginning.");

    private static Error NotYours => Error.Validation(
        SyncErrorCodes.SyncCursorNotYours,
        "That cursor belongs to another owner.");

    private bool TryRead(string cursor, out CursorPayload payload)
    {
        payload = default!;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        var parts = cursor.Split('.');
        if (parts.Length != 3 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryDecode(parts[1], out var body) || !TryDecode(parts[2], out var signature))
        {
            return false;
        }

        // Fixed-time, so a caller cannot learn the expected signature one byte
        // at a time by measuring how long the rejection took. It also handles the
        // length mismatch, which an equality check would leak on its own.
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, body), signature))
        {
            return false;
        }

        try
        {
            var read = JsonSerializer.Deserialize<CursorPayload>(body, PayloadFormat);

            payload = read!;
            return read is { Owner.Length: > 0, Continuation: not null };
        }
        catch (JsonException)
        {
            // Only reachable if the key ever signed something that is not this
            // payload, so it is a bug rather than an attack — but a bug that must
            // still answer 400 rather than 500 to the device that hit it.
            return false;
        }
    }

    /// <summary><c>IsValid</c> first because <c>TryDecodeFromChars</c> throws on
    /// an illegal character rather than answering false, and a cursor a client
    /// typed by hand is expected input here, not an exceptional one.</summary>
    private static bool TryDecode(string segment, out byte[] bytes)
    {
        var buffer = new byte[Base64Url.GetMaxDecodedLength(segment.Length)];

        if (Base64Url.IsValid(segment) && Base64Url.TryDecodeFromChars(segment, buffer, out var written))
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = [];
        return false;
    }

    /// <summary>
    /// The signed body. Short names because a cursor is carried in a query
    /// string on every pull and the JSON is never read by a person:
    /// <c>o</c> the owner, <c>c</c> the store's continuation, <c>i</c> when it
    /// was issued. The issue time is not enforced here — the store is the only
    /// thing that knows how far back its feed still reaches, and it says so with
    /// <c>sync.cursor_expired</c> — but it is signed, so a stale cursor can be
    /// recognised in a log line.
    /// </summary>
    private sealed record CursorPayload(
        [property: JsonPropertyName("o")] string Owner,
        [property: JsonPropertyName("c")] string Continuation,
        [property: JsonPropertyName("i")] long IssuedAt);
}
