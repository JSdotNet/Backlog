using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Modules.Sync.Services;

/// <summary>Mints the short code a person carries between two devices.</summary>
public interface IPairingCodeGenerator
{
    /// <summary>A new code, normalized and well-formed per
    /// <see cref="PairingCodeFormat"/>.</summary>
    string Next();
}

/// <summary>
/// Eight symbols drawn from <see cref="PairingCodeFormat.Alphabet"/> by
/// <see cref="RandomNumberGenerator.GetItems{T}(ReadOnlySpan{T}, int)"/>, which
/// is uniform over the alphabet and cryptographically seeded. A code that is
/// only worth guessing for ten minutes still has to be unguessable for those
/// ten minutes, and <c>Random</c> would not be.
/// </summary>
public sealed class RandomPairingCodeGenerator : IPairingCodeGenerator
{
    public string Next() =>
        new(RandomNumberGenerator.GetItems<char>(PairingCodeFormat.Alphabet, PairingCodeFormat.Length));
}
