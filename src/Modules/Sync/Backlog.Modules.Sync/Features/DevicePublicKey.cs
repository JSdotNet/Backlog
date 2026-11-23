using System.Buffers.Text;
using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Features;

/// <summary>
/// The one rule every way of binding a key shares — registration, pairing and the
/// migration on a token fetch (local ADR 0058 §2): the key is a base64url
/// SubjectPublicKeyInfo of a P-256 key and nothing else. What is stored is the
/// key re-encoded, so two spellings of one key cannot both be on file.
/// </summary>
internal static class DevicePublicKey
{
    /// <summary>Far above the 91 bytes a P-256 SubjectPublicKeyInfo takes, and low
    /// enough that an anonymous caller cannot make the service decode much.</summary>
    private const int MaxLength = 512;

    internal static Result<string> Validate(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.Length is > 0 and <= MaxLength && TryImport(candidate) is { } key)
        {
            using (key)
            {
                return Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
            }
        }

        return Error.Validation(
            SyncErrorCodes.DevicePublicKeyInvalid,
            "A device key is a base64url SubjectPublicKeyInfo of a P-256 key.");
    }

    /// <summary>The key, or null when <paramref name="encoded"/> is not a whole P-256
    /// SubjectPublicKeyInfo. The caller disposes what it gets.</summary>
    internal static ECDsa? TryImport(string encoded)
    {
        byte[] bytes;

        try
        {
            bytes = Base64Url.DecodeFromChars(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        var key = ECDsa.Create();

        try
        {
            key.ImportSubjectPublicKeyInfo(bytes, out var read);

            if (read == bytes.Length && IsP256(key.ExportParameters(includePrivateParameters: false).Curve))
            {
                return key;
            }
        }
        catch (CryptographicException)
        {
            // Not a key, or one the platform cannot read: refused the same way.
        }

        key.Dispose();
        return null;
    }

    private static bool IsP256(ECCurve curve)
    {
        var p256 = ECCurve.NamedCurves.nistP256.Oid;

        // Windows names the imported curve by its friendly name and may leave the
        // value empty; other platforms fill the value in.
        return curve.IsNamed
            && (string.Equals(curve.Oid.Value, p256.Value, StringComparison.Ordinal)
                || string.Equals(curve.Oid.FriendlyName, p256.FriendlyName, StringComparison.OrdinalIgnoreCase));
    }
}
