using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Builds the <c>private_key_jwt</c> assertion (RFC 7523 §3) a device bound to a key
/// trades for a token, in the shape <see cref="SyncAssertion"/> fixes (local ADR 0058
/// §2). Written by hand rather than through a JWT library: the shape is one header and
/// six claims, and the service reads it by hand for the same reason.
/// </summary>
public static class DeviceAssertion
{
    /// <summary>How long an assertion is good for: long enough for a slow request,
    /// well inside <see cref="SyncAssertion.MaxLifetime"/>.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private static readonly byte[] Header = """{"alg":"ES256","typ":"JWT"}"""u8.ToArray();

    /// <summary>A compact JWT for <paramref name="deviceId"/>, signed with
    /// <paramref name="key"/>, issued now and expiring <see cref="Lifetime"/> later,
    /// with a fresh random <c>jti</c> so the service can refuse a replay.</summary>
    public static string Create(Guid deviceId, IDeviceKey key, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(time);

        var issuedAt = time.GetUtcNow();
        var device = deviceId.ToString("D");

        using var claims = new MemoryStream();
        using (var writer = new Utf8JsonWriter(claims))
        {
            writer.WriteStartObject();
            writer.WriteString("iss", device);
            writer.WriteString("sub", device);
            writer.WriteString("aud", SyncAssertion.Audience);
            writer.WriteString("jti", Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
            writer.WriteNumber("iat", issuedAt.ToUnixTimeSeconds());
            writer.WriteNumber("exp", issuedAt.Add(Lifetime).ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        var signingInput = Base64Url.EncodeToString(Header) + "." + Base64Url.EncodeToString(claims.ToArray());
        var signature = key.Sign(Encoding.ASCII.GetBytes(signingInput));

        return signingInput + "." + Base64Url.EncodeToString(signature);
    }
}
