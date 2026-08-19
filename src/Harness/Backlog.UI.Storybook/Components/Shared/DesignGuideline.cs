using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// A rule from `.design` that governs a storybook page, and the text of it.
///
/// <para>The storybook says how a component is used; it does not say what the
/// rules are. Those live in `.design`, which is the checked-in, reviewable record
/// of the product's design and UX guidelines — and the two used to disagree,
/// because a rule written into a story description is a second copy of a rule
/// nobody diffs against the first.</para>
///
/// <para>The chapter is <b>rendered here</b> rather than linked. A link is a
/// promise that somebody will open it, and a reviewer comparing a component
/// against a rule will not leave the page to do it — so the rule is drawn beside
/// the thing it governs, out of the repository's own file, with no copy in
/// between. The files are embedded (see the csproj) so this works wherever the
/// host runs.</para>
/// </summary>
/// <param name="Path">The chapter, and optionally its anchor, relative to
/// `.design` — <c>color-scheme.md#role-tokens</c>. The anchor is a GitHub-style
/// heading slug, so it is the same string that works in a link.</param>
/// <param name="Governs">What that chapter decides for this page, in one line.
/// Not a summary of the chapter: the reason to read it.</param>
public sealed record DesignGuideline(string Path, string Governs)
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>The repository-relative address, which is what a reader greps
    /// for and what the fold is labelled with.</summary>
    public string Label => ".design/" + Path;

    private string File => Path.Split('#')[0];

    private string? Anchor => Path.Contains('#') ? Path.Split('#', 2)[1] : null;

    /// <summary>The markdown of the section this rule names — the anchored
    /// heading and everything under it, up to the next heading at the same level
    /// or above. Without an anchor, the whole chapter.
    /// <para>
    /// A section that cannot be found reads back as a sentence saying so rather
    /// than as an empty fold: a rule this page claims to obey and that is not
    /// there any more is a finding, and a silent blank would hide it.
    /// </para></summary>
    public string Markdown => Cache.GetOrAdd(Path, _ => Load());

    private string Load()
    {
        var chapter = ReadEmbedded(File);

        if (chapter is null)
        {
            return $"> `.design/{File}` is not embedded in this host, so the rule cannot be shown here. "
                 + "Read it in the repository.";
        }

        if (Anchor is null) return chapter;

        var section = Section(chapter, Anchor);

        return section
            ?? $"> `.design/{File}` has no heading matching `#{Anchor}` any more. "
             + "Either the heading was renamed or this page is pointing at a rule that has moved.";
    }

    private static string? ReadEmbedded(string file)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream($"design/{file}");
        if (stream is null) return null;

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>The lines from the heading whose slug is <paramref name="anchor"/>
    /// to the one that ends it. Trailing blank lines go, so a section does not
    /// arrive with the gap that separated it from the next one.</summary>
    private static string? Section(string chapter, string anchor)
    {
        var lines = chapter.Replace("\r\n", "\n").Split('\n');
        var start = -1;
        var level = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var heading = Heading(lines[index]);
            if (heading is null) continue;

            if (start < 0)
            {
                if (!string.Equals(Slug(heading.Value.Text), anchor, StringComparison.OrdinalIgnoreCase)) continue;

                start = index;
                level = heading.Value.Level;
                continue;
            }

            // The next heading at the same depth or shallower is where this
            // section stops. A deeper one is part of it.
            if (heading.Value.Level > level) continue;

            return Join(lines, start, index);
        }

        return start < 0 ? null : Join(lines, start, lines.Length);
    }

    private static string Join(string[] lines, int start, int end)
    {
        var text = new StringBuilder();

        for (var index = start; index < end; index++)
        {
            text.Append(lines[index]).Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    private static (int Level, string Text)? Heading(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;

        if (hashes is 0 or > 6) return null;
        if (hashes >= line.Length || line[hashes] != ' ') return null;

        return (hashes, line[(hashes + 1)..].Trim());
    }

    /// <summary>GitHub's heading slug: lower-cased, punctuation dropped, spaces
    /// to hyphens. It has to be GitHub's rather than a tidier one of our own,
    /// because these anchors are also written into `.design`'s own cross-links
    /// and into pull-request comments, where GitHub is the thing resolving them.
    /// <para>
    /// Inline code and emphasis markers are punctuation as far as the slug is
    /// concerned — <c>## `meta` blocks</c> slugs to <c>meta-blocks</c> — so they
    /// are dropped rather than turned into hyphens.
    /// </para></summary>
    private static string Slug(string heading)
    {
        var slug = new StringBuilder(heading.Length);

        foreach (var character in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (character is ' ' or '-')
            {
                slug.Append('-');
            }

            // Everything else — backticks, brackets, slashes, em dashes, colons
            // — contributes nothing, which is what turns "Screen Reader /
            // Announcements" into "screen-reader--announcements".
        }

        return slug.ToString();
    }
}
