namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// A link from a Brain note into a repository devbook (local ADR 0027 §6),
/// written <c>repo:&lt;alias&gt;/&lt;devbook path&gt;[#slug]</c> in a note's
/// <c>related</c> or in its body.
/// <para>
/// Read here, once, because three readers need the same reading: the Brain note
/// that draws the link as a chip, the resolver that asks whether this machine
/// has the repository, and the backlink query that finds the notes pointing at
/// a repository chapter. The alias is everything before the first <c>/</c>: an
/// alias is a short name and never holds one, while the devbook path after it
/// always does.
/// </para>
/// </summary>
/// <param name="Raw">The link as it was written, scheme included.</param>
/// <param name="Alias">The repository alias, as written.</param>
/// <param name="Path">The repository-relative devbook path, with forward
/// slashes — <c>.devbook/arc42/05-building-block-view.md</c>.</param>
/// <param name="Slug">The heading slug, or null for the whole file.</param>
public sealed record BrainRepoLink(string Raw, string Alias, string Path, string? Slug)
{
    /// <summary>The scheme every link into a repository devbook starts with.</summary>
    public const string Scheme = "repo:";

    /// <summary>The path and slug back together, the way a repository devbook
    /// reference is written: <c>path</c> or <c>path#slug</c>.</summary>
    public string Reference => Slug is null ? Path : $"{Path}#{Slug}";

    /// <summary>Whether <paramref name="text"/> is written in the
    /// <c>repo:</c> scheme at all, well formed or not.</summary>
    public static bool IsRepoLink(string? text) =>
        text is not null && Unquote(text).StartsWith(Scheme, StringComparison.Ordinal);

    /// <summary>
    /// Reads a <c>repo:</c> link, or answers null when the text is not one or
    /// names no alias or no path. Quotes a YAML list may leave round an entry are
    /// dropped, and a leading <c>./</c> or <c>/</c> on the path is too, so two
    /// spellings of one chapter compare equal.
    /// </summary>
    public static BrainRepoLink? Parse(string? text)
    {
        if (text is null) return null;

        var raw = Unquote(text);
        if (!raw.StartsWith(Scheme, StringComparison.Ordinal)) return null;

        var rest = raw[Scheme.Length..];
        var separator = rest.IndexOf('/');
        if (separator <= 0) return null;

        var alias = rest[..separator].Trim();
        var target = rest[(separator + 1)..].Replace('\\', '/');

        string? slug = null;
        var hash = target.IndexOf('#');
        if (hash >= 0)
        {
            var anchor = target[(hash + 1)..].Trim();
            slug = anchor.Length == 0 ? null : anchor;
            target = target[..hash];
        }

        var path = target.Trim();
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        path = path.TrimStart('/');

        return alias.Length == 0 || path.Length == 0 ? null : new BrainRepoLink(raw, alias, path, slug);
    }

    /// <summary>Whether this link names the chapter file at
    /// <paramref name="chapterPath"/> in the repository
    /// <paramref name="repositoryAlias"/> — either devbook layout's spelling, any
    /// heading in it.</summary>
    public bool Names(string repositoryAlias, string chapterPath) =>
        string.Equals(Alias, repositoryAlias, StringComparison.OrdinalIgnoreCase)
        && string.Equals(DevbookLayout.ConventionalPath(Path), DevbookLayout.ConventionalPath(chapterPath), StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 && (trimmed[0] is '"' or '\'') && trimmed[^1] == trimmed[0]
            ? trimmed[1..^1].Trim()
            : trimmed;
    }
}
