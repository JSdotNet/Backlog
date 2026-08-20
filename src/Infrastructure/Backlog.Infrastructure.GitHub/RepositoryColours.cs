namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Which identity hue each configured repository wears.
/// <para>
/// One algorithm, in one place, because four surfaces read the answer — the header
/// filter, the roadmap band, an entry row and an agent session row — and the same
/// project has to be the same colour on all of them.
/// <c>.design/color-scheme.md#band-identity-tokens</c> says so in as many words: a
/// surface that worked its own hue out would be a second identity for the same thing.
/// </para>
/// <para>
/// A number, never a colour. This says <em>which</em> of the sanctioned hues; the
/// values belong to the stylesheet and nothing here ever sees one.
/// </para>
/// </summary>
public static class RepositoryColours
{
    /// <summary>How many hues the design system sanctions. Stated here because this is
    /// what validates a choice, and because the control that offers the choice has to
    /// offer exactly these and no more.</summary>
    public const int Available = 5;

    /// <summary>Whether a stored number names one of the sanctioned hues. A choice
    /// outside the range is dropped rather than clamped: clamping would silently give
    /// somebody a hue they did not ask for and make it look like a choice they had
    /// made.</summary>
    public static bool IsSanctioned(int? colour) => colour is >= 1 and <= Available;

    /// <summary>
    /// The effective hue for every configured repository, keyed by alias.
    /// <para>
    /// An explicit choice wins. Everything else is taken by position, stepping over
    /// the hues an explicit choice has already claimed so an automatic repository never
    /// lands on the hue its neighbour was deliberately given. Past five it wraps and
    /// collisions become unavoidable, which the design section allows for the reason it
    /// always gives: the hue is not the identifier, the alias is.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, int> Resolve(IEnumerable<GitHubRepositoryRef>? repositories)
    {
        var configured = repositories?.ToList() ?? [];
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        var claimed = configured
            .Where(repository => IsSanctioned(repository.Colour))
            .Select(repository => repository.Colour!.Value)
            .ToHashSet();

        var next = 1;

        foreach (var repository in configured)
        {
            resolved[repository.Alias] = IsSanctioned(repository.Colour)
                ? repository.Colour!.Value
                : NextFree(ref next, claimed);
        }

        return resolved;
    }

    /// <summary>The next hue no repository has explicitly claimed, advancing the
    /// counter past it. Falls through to the plain next one once every hue is spoken
    /// for, which is what wrapping means with more repositories than hues.</summary>
    private static int NextFree(ref int next, HashSet<int> claimed)
    {
        for (var tried = 0; tried < Available; tried++)
        {
            var candidate = Wrap(next++);
            if (!claimed.Contains(candidate)) return candidate;
        }

        return Wrap(next++);
    }

    private static int Wrap(int position) => ((position - 1) % Available) + 1;
}
