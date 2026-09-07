using System.Security.Cryptography;
using System.Text;

namespace Backlog.Modules.Sync.Services;

/// <summary>How a secret is stored, and how a presented one is checked against
/// what was stored.</summary>
public interface ICredentialHasher
{
    /// <summary>The stored form of a secret, as lower-case hex.</summary>
    string Hash(string secret);

    /// <summary>Whether <paramref name="secret"/> is the one
    /// <paramref name="expectedHash"/> was made from.</summary>
    bool Matches(string secret, string expectedHash);
}

/// <summary>
/// Plain SHA-256, no salt and no work factor — which is the right answer here
/// and the wrong one for a password.
/// <para>
/// A password hash is slow on purpose because a password is short, memorable,
/// and probably reused: the attacker's advantage is a dictionary, and PBKDF2 or
/// Argon2 exists to price each guess. Neither registration credentials nor
/// pairing codes are chosen by a person. A registration credential is 256 bits
/// from <see cref="RandomNumberGenerator"/>, so there is no dictionary to price
/// and no rainbow table to salt against — 2^256 guesses is out of reach whether
/// each one costs a nanosecond or a second. Adding a work factor would buy
/// nothing and cost a slow hash on every sync, which is the one call that
/// happens constantly.
/// </para>
/// <para>
/// A pairing code is much smaller — about 8.5 x 10^11 — but it is also
/// single-use and lives ten minutes, so what limits guessing is the window and
/// not the hash.
/// </para>
/// <para>
/// The comparison is <see cref="CryptographicOperations.FixedTimeEquals"/> and
/// not <c>==</c>. String equality returns as soon as two bytes differ, so how
/// long it took is a measurement of how much of the secret was right; a caller
/// who can time enough attempts can walk a credential out one byte at a time.
/// The fixed-time compare removes that channel, which is the part of this class
/// that is genuinely load-bearing.
/// </para>
/// </summary>
public sealed class Sha256CredentialHasher : ICredentialHasher
{
    public string Hash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    public bool Matches(string secret, string expectedHash)
    {
        if (secret is null || string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(secret)),
            Encoding.UTF8.GetBytes(expectedHash));
    }
}
