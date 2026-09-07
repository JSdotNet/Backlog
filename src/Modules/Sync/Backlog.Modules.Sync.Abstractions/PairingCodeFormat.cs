namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// The shape of a pairing code, which is a format a person reads off one screen
/// and types into another rather than a string a machine passes along.
/// <para>
/// The alphabet is Crockford-style: no <c>0</c>/<c>O</c>, no <c>1</c>/<c>I</c>/
/// <c>L</c>, no <c>U</c>. Those are the pairs somebody mistypes when copying by
/// eye, and dropping them costs almost nothing — thirty-one symbols over eight
/// characters is still about 8.5 x 10^11 codes, against a window of ten minutes.
/// </para>
/// <para>
/// It lives in the published surface because both halves of the exchange need
/// the same answer: the service mints a code from this alphabet, and the client
/// that asks somebody to type one back in normalizes with the same rules. Two
/// copies of an alphabet is two things to keep in step.
/// </para>
/// </summary>
public static class PairingCodeFormat
{
    /// <summary>The symbols a code is drawn from.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>How many symbols a code has.</summary>
    public const int Length = 8;

    /// <summary>
    /// What somebody typed, reduced to what the service stores: upper-cased,
    /// with everything outside <see cref="Alphabet"/> dropped. That takes the
    /// hyphen out of a code pasted back in its displayed form, and the spaces
    /// out of one read aloud.
    /// <para>
    /// Deliberately not a spell-corrector: a typed <c>O</c> is dropped rather
    /// than folded to <c>0</c>, because folding would make two different codes
    /// normalize alike and the redemption lookup is by hash.
    /// </para>
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var kept = new char[input.Length];
        var count = 0;

        foreach (var character in input)
        {
            var upper = char.ToUpperInvariant(character);
            if (Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                kept[count++] = upper;
            }
        }

        return new string(kept, 0, count);
    }

    /// <summary>
    /// Whether an already-<see cref="Normalize">normalized</see> string could be
    /// a code. It says nothing about whether that code was ever issued — that
    /// question costs a lookup, and this one exists so a typo does not.
    /// </summary>
    public static bool IsWellFormed(string normalized) =>
        normalized is { Length: Length } && normalized.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>
    /// The code as it is shown to a person: two groups of four. Chunking is for
    /// reading it out loud and keeping your place; the hyphen is not part of the
    /// code and <see cref="Normalize"/> takes it straight back out.
    /// </summary>
    public static string Display(string normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);

        return normalized.Length == Length
            ? string.Concat(normalized[..4], "-", normalized[4..])
            : normalized;
    }
}
