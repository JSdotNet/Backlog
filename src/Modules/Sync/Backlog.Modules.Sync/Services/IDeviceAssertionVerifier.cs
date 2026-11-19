using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features;

namespace Backlog.Modules.Sync.Services;

/// <summary>An assertion that passed every check but the replay one, which needs a
/// store: its <c>jti</c>, and the last moment it could still be taken.</summary>
public sealed record VerifiedAssertion(string Id, DateTimeOffset AcceptableUntil);

/// <summary>Checks a device's <c>private_key_jwt</c> assertion (RFC 7523 §3) against
/// the public key the device registered (local ADR 0058 §2).</summary>
public interface IDeviceAssertionVerifier
{
    /// <summary>The verified assertion, or null when <paramref name="assertion"/> is
    /// not one <paramref name="deviceId"/> signed with <paramref name="publicKey"/> for
    /// this service, now. Every failure reads the same.</summary>
    VerifiedAssertion? Verify(string assertion, DeviceId deviceId, string publicKey);
}

/// <summary>
/// ES256 only, read by hand rather than through a JWT library: the shape is fixed by
/// <see cref="SyncAssertion"/>, and a library's job of accepting many algorithms and
/// key types is the opposite of what is wanted here.
/// <para>
/// The header must name ES256, carry no <c>crit</c>, and say <c>typ</c> JWT or
/// nothing. The signature is checked before any claim is read, so nothing an
/// unsigned body says decides anything. Then: <c>iss</c> and <c>sub</c> both the device,
/// <c>aud</c> <see cref="SyncAssertion.Audience"/>, <c>exp</c> not past and
/// <c>iat</c> not ahead by more than <see cref="ClockSkew"/>, <c>exp</c> after
/// <c>iat</c> by at most <see cref="SyncAssertion.MaxLifetime"/>, and a <c>jti</c>.
/// </para>
/// </summary>
public sealed class EcdsaDeviceAssertionVerifier(TimeProvider clock) : IDeviceAssertionVerifier
{
    /// <summary>How far a device's clock may be off the service's either way. Two
    /// minutes, so a laptop whose clock has drifted since it last synced its time still
    /// signs in; an assertion's own lifetime stays capped at
    /// <see cref="SyncAssertion.MaxLifetime"/>, and its id is held as spent until
    /// <c>exp</c> plus this skew, the last moment it could be taken.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    /// <summary>Far above what an ES256 assertion with these claims takes, and low
    /// enough that an anonymous caller cannot make the service decode much.</summary>
    private const int MaxLength = 2048;

    /// <summary>The replay table holds every <c>jti</c> for minutes; a bound keeps one
    /// caller from filling it with long ones.</summary>
    private const int MaxIdLength = 128;

    private const int SignatureLength = 64;

    public VerifiedAssertion? Verify(string assertion, DeviceId deviceId, string publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        if (string.IsNullOrEmpty(assertion) || assertion.Length > MaxLength)
        {
            return null;
        }

        var parts = assertion.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
            if (!HeaderAccepted(header.RootElement))
            {
                return null;
            }

            if (!SignedBy(parts, publicKey))
            {
                return null;
            }

            using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return Claims(claims.RootElement, deviceId);
        }
        catch (Exception failure) when (failure is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool SignedBy(string[] parts, string publicKey)
    {
        var signature = Base64Url.DecodeFromChars(parts[2]);
        if (signature.Length != SignatureLength)
        {
            return false;
        }

        using var key = DevicePublicKey.TryImport(publicKey);

        return key is not null && key.VerifyData(
            Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private VerifiedAssertion? Claims(JsonElement claims, DeviceId deviceId)
    {
        if (claims.ValueKind is not JsonValueKind.Object
            || !NamesDevice(claims, "iss", deviceId)
            || !NamesDevice(claims, "sub", deviceId)
            || !HasAudience(claims)
            || !claims.TryGetProperty("jti", out var jti)
            || jti.ValueKind is not JsonValueKind.String
            || jti.GetString() is not { Length: > 0 and <= MaxIdLength } id
            || Time(claims, "iat") is not { } issuedAt
            || Time(claims, "exp") is not { } expiresAt)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        // The bounds on iat and the lifetime come first: together they hold exp
        // within minutes of now, so the addition below cannot run past the end of
        // the calendar however far out a caller writes exp.
        if (issuedAt > now + ClockSkew
            || expiresAt <= issuedAt
            || expiresAt - issuedAt > SyncAssertion.MaxLifetime)
        {
            return null;
        }

        var acceptableUntil = expiresAt + ClockSkew;

        return acceptableUntil <= now ? null : new VerifiedAssertion(id, acceptableUntil);
    }

    /// <summary><c>alg</c> ES256, <c>typ</c> absent or <c>JWT</c>, and no <c>crit</c>:
    /// a critical extension is one the signer requires the reader to understand
    /// (RFC 7515 §4.1.11), and this reader understands none.</summary>
    private static bool HeaderAccepted(JsonElement header) =>
        IsString(header, "alg", SyncAssertion.Algorithm)
        && !header.TryGetProperty("crit", out _)
        && (!header.TryGetProperty("typ", out _) || IsString(header, "typ", "JWT"));

    private static bool IsString(JsonElement element, string name, string expected) =>
        element.ValueKind is JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool NamesDevice(JsonElement claims, string name, DeviceId deviceId) =>
        claims.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var named)
        && named == deviceId.Value;

    /// <summary><c>aud</c> as a single string or an array holding it (RFC 7519 §4.1.3).</summary>
    private static bool HasAudience(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        return audience.ValueKind switch
        {
            JsonValueKind.String => string.Equals(audience.GetString(), SyncAssertion.Audience, StringComparison.Ordinal),
            JsonValueKind.Array => audience.EnumerateArray().Any(entry =>
                entry.ValueKind is JsonValueKind.String
                && string.Equals(entry.GetString(), SyncAssertion.Audience, StringComparison.Ordinal)),
            _ => false,
        };
    }

    /// <summary>A NumericDate (RFC 7519 §2): whole seconds, fractions allowed and dropped.</summary>
    private static DateTimeOffset? Time(JsonElement claims, string name)
    {
        if (!claims.TryGetProperty(name, out var value) || value.ValueKind is not JsonValueKind.Number)
        {
            return null;
        }

        if (!value.TryGetDouble(out var seconds) || double.IsNaN(seconds) || seconds < 0 || seconds > 253402300799)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }
}
