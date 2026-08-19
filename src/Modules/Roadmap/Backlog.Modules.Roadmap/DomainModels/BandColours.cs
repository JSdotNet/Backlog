namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// Which colour each repository's band has been given, for the repositories somebody
/// has actually chosen for.
/// <para>
/// A first-class collection: the plan holds one of these and nothing else about
/// colour, so there is one place that knows a choice is an index into a sanctioned set
/// rather than a colour of its own. The plan stores <em>which</em> of the approved
/// hues, never a hue — inventing a colour is a design decision and
/// <c>.design/color-scheme.md#band-identity-tokens</c> is where it is made.
/// </para>
/// <para>
/// A repository absent from here is not an error and not a default: it means nobody
/// has chosen, and the view is free to place it in whatever hue is still going. That
/// is why this is a sparse map rather than a colour per configured repository — a
/// plan should not have to be rewritten because a repository was added to Settings.
/// </para>
/// </summary>
public sealed class BandColours
{
    /// <summary>How many hues the design system sanctions for bands. Stated here
    /// because this is what validates a choice; the values themselves belong to the
    /// stylesheet and this module never sees one.</summary>
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

    internal void Choose(string alias, int colour) => _chosen[alias] = colour;

    internal void Forget(string alias) => _chosen.Remove(alias);
}
