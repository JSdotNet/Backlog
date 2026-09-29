namespace Backlog.SharedKernel;

/// <summary>
/// One enum's vocabulary on the wire and in the store: each member's token, and
/// the way back from a token to the member.
/// <para>
/// Tokens rather than ordinals, because a database somebody opens should read as
/// the domain reads, and an ordinal silently changes meaning the day a member is
/// inserted. The tokens themselves belong to each module; what they all share is
/// here — a token is read whatever its case, surrounding space, underscores or
/// hyphens, and one this build does not know throws rather than being coerced to
/// a plausible member.
/// </para>
/// <para>
/// A caller that has an honest default for an unknown token names it as the
/// fallback on <see cref="Parse(string?, TEnum?)"/>. That is a decision about
/// one vocabulary, taken where the vocabulary is, never a default of the map.
/// </para>
/// </summary>
/// <typeparam name="TEnum">The enum the tokens name.</typeparam>
public sealed class WireTokenMap<TEnum>
    where TEnum : struct, Enum
{
    private readonly string _name;
    private readonly Dictionary<TEnum, string> _tokens;
    private readonly Dictionary<string, TEnum> _members = new(StringComparer.Ordinal);

    /// <param name="name">What a token names, as an unknown one is reported:
    /// <c>Unknown {name} '{token}'.</c></param>
    /// <param name="tokens">Each member's token, exactly as it is written.</param>
    /// <param name="aliases">Tokens that are read but never written — a spelling
    /// an older build wrote for a member that has since been renamed.</param>
    /// <exception cref="ArgumentException">Two tokens or aliases read the
    /// same.</exception>
    public WireTokenMap(
        string name,
        IReadOnlyDictionary<TEnum, string> tokens,
        IReadOnlyDictionary<string, TEnum>? aliases = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(tokens);

        _name = name;
        _tokens = new Dictionary<TEnum, string>(tokens);

        foreach (var (member, token) in tokens) _members.Add(Normalize(token), member);
        foreach (var (alias, member) in aliases ?? new Dictionary<string, TEnum>()) _members.Add(Normalize(alias), member);
    }

    /// <summary>The token <paramref name="value"/> is written as.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The member has no
    /// token.</exception>
    public string ToWire(TEnum value) =>
        _tokens.TryGetValue(value, out var token) ? token : throw new ArgumentOutOfRangeException(nameof(value));

    /// <summary>The member <paramref name="value"/> names, or
    /// <paramref name="fallback"/> when it names none and the caller gave one.</summary>
    /// <exception cref="FormatException">The token is unknown and no fallback was
    /// given.</exception>
    public TEnum Parse(string? value, TEnum? fallback = null) =>
        _members.TryGetValue(Normalize(value), out var member) ? member
        : fallback ?? throw new FormatException($"Unknown {_name} '{value}'.");

    /// <summary>The form a token is compared in: trimmed, lower-case, with no
    /// underscores or hyphens, so <c>in_progress</c>, <c>In-Progress</c> and
    /// <c>inprogress</c> are one token.</summary>
    public static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
}
