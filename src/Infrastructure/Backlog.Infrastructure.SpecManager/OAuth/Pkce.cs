using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.SpecManager.OAuth;

/// <summary>
/// The PKCE pair (RFC 7636) and the <c>state</c> value one sign-in sends. S256
/// only: spec-manager offers no other method, and OAuth 2.1 dropped <c>plain</c>.
/// </summary>
internal static class Pkce
{
    public const string Method = "S256";

    /// <summary>Thirty-two random bytes, base64url without padding: the 43
    /// characters RFC 7636 §4.1 asks for at least.</summary>
    public static string CreateVerifier() => Random(32);

    /// <summary>The S256 challenge for <paramref name="verifier"/>: base64url of
    /// the SHA-256 of its ASCII bytes.</summary>
    public static string Challenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>An unguessable value tying the answer that comes back to the request
    /// that went out.</summary>
    public static string CreateState() => Random(24);

    private static string Random(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
}
