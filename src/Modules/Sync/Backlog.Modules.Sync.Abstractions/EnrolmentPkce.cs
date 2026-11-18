using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The rules the owner sign-in's two ends must agree on (local ADR 0052 §1): PKCE
/// with S256 (RFC 7636) and a loopback redirect (RFC 8252 §7.3). Written once, so
/// the desktop that builds a challenge and the service that checks it cannot drift.
/// </summary>
public static class EnrolmentPkce
{
    /// <summary>The one challenge method accepted. <c>plain</c> would hand the
    /// verifier to anyone who saw the start.</summary>
    public const string Method = "S256";

    /// <summary>A fresh verifier: 32 random bytes, base64url, 43 characters.</summary>
    public static string CreateVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge of a verifier.</summary>
    public static string Challenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>Whether a verifier has the shape RFC 7636 §4.1 gives it: 43 to 128
    /// characters of the unreserved set.</summary>
    public static bool IsWellFormedVerifier(string? verifier) =>
        verifier is { Length: >= 43 and <= 128 }
        && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~');

    /// <summary>Whether a challenge is what S256 produces: 43 base64url characters.</summary>
    public static bool IsWellFormedChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Whether the verifier hashes to the challenge, compared in constant time.</summary>
    public static bool Matches(string verifier, string challenge)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(challenge);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Challenge(verifier)),
            Encoding.ASCII.GetBytes(challenge));
    }

    /// <summary>
    /// Whether a redirect is one a native app listens on: <c>http</c>, a loopback
    /// IP literal, an explicit port, and no fragment. Not <c>localhost</c>, which
    /// RFC 8252 §8.3 advises against because a resolver can send it elsewhere.
    /// Anything else would let a sign-in hand its code to some other host.
    /// </summary>
    public static bool IsLoopbackRedirect(Uri? redirect)
    {
        if (redirect is not { IsAbsoluteUri: true }
            || redirect.Scheme != Uri.UriSchemeHttp
            || !string.IsNullOrEmpty(redirect.Fragment)
            || !string.IsNullOrEmpty(redirect.UserInfo)
            || redirect.IsDefaultPort)
        {
            return false;
        }

        return IPAddress.TryParse(redirect.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }
}
