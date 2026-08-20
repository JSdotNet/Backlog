namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// Which colour each repository's band was given, in plans written before the choice
/// moved to Settings.
/// <para>
/// <strong>Legacy, read-only.</strong> A repository's identity hue is now a fact about
/// the repository rather than about one plan, and it is chosen once in Settings so the
/// roadmap, the filter and the entry list cannot disagree about it — see
/// <c>.design/color-scheme.md#band-identity-tokens</c>. This type survives so a plan
/// file written before that still parses and so the choices in it can be carried over;
/// nothing writes it any more.
/// </para>
/// <para>
/// A repository absent from here is not an error and not a default: it means nobody
/// had chosen when the file was written.
/// </para>
/// </summary>
public sealed class BandColours
{
    /// <summary>How many hues the design system sanctions. Kept here because it is
    /// what validates a stored choice while one is being read; the live definition is
    /// <c>RepositoryColours.Available</c>, and the values themselves belong to the
    /// stylesheet — this module never sees one.</summary>
    public const int Available = 5;

    private readonly Dictionary<string, int> _chosen;

    private BandColours(Dictionary<string, int> chosen) => _chosen = chosen;

    public static BandColours None() => new([]);

    /// <summary>
    /// The choices as stored, keeping only the ones that name a colour in the
    /// sanctioned range.
    /// <para>
    /// A file naming colour 9 for a repository is dropped rather than clamped: clamping
    /// would silently give somebody a hue they did not ask for and make it look like a
    /// choice they had made.
    /// </para>
    /// </summary>
    public static BandColours Of(IEnumerable<KeyValuePair<string, int>>? chosen)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (alias, colour) in chosen ?? [])
        {
            var normalized = RepositoryScope.Normalize(alias);
            if (normalized.Length == 0 || colour is < 1 or > Available) continue;

            map[normalized] = colour;
        }

        return new BandColours(map);
    }

    public IReadOnlyDictionary<string, int> Chosen => _chosen;

    public int Count => _chosen.Count;

    /// <summary>The colour chosen for a repository, or null when nobody has chosen
    /// one.</summary>
    public int? For(string? alias)
    {
        var normalized = RepositoryScope.Normalize(alias);
        return normalized.Length > 0 && _chosen.TryGetValue(normalized, out var colour) ? colour : null;
    }
}
