using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Tests;

/// <summary>
/// A device's P-256 key in software, the way the harness keeps one, and the
/// <c>private_key_jwt</c> assertions it signs (local ADR 0058 §2). Every claim can be
/// bent on purpose, so a test can build the assertion the service must refuse.
/// </summary>
internal sealed class DeviceSigningKey : IDisposable
{
    private readonly ECDsa _key;

    internal DeviceSigningKey(ECCurve? curve = null)
    {
        _key = ECDsa.Create(curve ?? ECCurve.NamedCurves.nistP256);
    }

    /// <summary>The public half as the service takes it: base64url SubjectPublicKeyInfo.</summary>
    internal string PublicKey => Base64Url.EncodeToString(_key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// An assertion for <paramref name="deviceId"/>, issued at <paramref name="issuedAt"/>
    /// and good for <paramref name="lifetime"/> — a minute unless a test says otherwise.
    /// </summary>
    /// <param name="type">The header's <c>typ</c>; null leaves it out.</param>
    /// <param name="critical">Adds a <c>crit</c> header naming <c>exp</c>.</param>
    /// <param name="signed">False leaves the signature segment empty, the way an
    /// <c>alg: none</c> token is written.</param>
    /// <param name="expiresAt">An <c>exp</c> in Unix seconds that overrides
    /// <paramref name="lifetime"/>, for values no lifetime reaches.</param>
    internal string Assertion(
        Guid deviceId,
        DateTimeOffset issuedAt,
        TimeSpan? lifetime = null,
        string audience = SyncAssertion.Audience,
        string? id = null,
        string algorithm = SyncAssertion.Algorithm,
        Guid? issuer = null,
        Guid? subject = null,
        string? type = "JWT",
        bool critical = false,
        bool signed = true,
        long? expiresAt = null)
    {
        var header = new Dictionary<string, object> { ["alg"] = algorithm };
        if (type is not null)
        {
            header["typ"] = type;
        }

        if (critical)
        {
            header["crit"] = new[] { "exp" };
        }

        var claims = new Dictionary<string, object>
        {
            ["iss"] = (issuer ?? deviceId).ToString("D"),
            ["sub"] = (subject ?? deviceId).ToString("D"),
            ["aud"] = audience,
            ["jti"] = id ?? Guid.NewGuid().ToString("N"),
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expiresAt ?? issuedAt.Add(lifetime ?? TimeSpan.FromMinutes(1)).ToUnixTimeSeconds(),
        };

        var signingInput = Encode(header) + "." + Encode(claims);
        if (!signed)
        {
            return signingInput + ".";
        }

        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    public void Dispose() => _key.Dispose();

    private static string Encode(Dictionary<string, object> part) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(part));
}
