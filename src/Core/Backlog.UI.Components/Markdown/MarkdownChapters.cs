namespace Backlog.UI.Components.Markdown;

/// <summary>
/// A chapter of a markdown file: a heading, and everything written under it,
/// verbatim.
/// </summary>
/// <param name="Level">How deep the heading was — one <c>#</c> is 1.</param>
/// <param name="Title">The heading text, without its hashes.</param>
/// <param name="Text">The heading line and the lines beneath it, as they were
/// written. Line endings are normalised to <c>\n</c> and the blank lines that
/// only separated this chapter from the next one are dropped; nothing else is
/// touched, because this is what someone pastes back into a file.</param>
public sealed record MarkdownChapter(int Level, string Title, string Text);

/// <summary>
/// Splits a markdown file into the chapters a reader would name.
/// <para>
/// Not a second parser, and it must not become one. <see cref="MarkdownPreview"/>
/// produces the blocks a view renders; this produces the *source* behind each
/// heading, which the blocks cannot give back — they carry no offsets, so there
/// is nothing to slice the file with once it has been parsed. The two are used
/// side by side: the parse is what is on screen, and this is what goes on the
/// clipboard.
/// </para>
/// <para>
/// A chapter runs to the next heading of the same or a higher level, so it
/// contains its own sub-chapters. Copying "Aggregate: Backlog Entry" and getting
/// its heading alone, with the entities beneath it left behind, would be a
/// surprising reading of the word — and the nesting is exactly why the fold
/// exists in the first place.
/// </para>
/// </summary>
public static class MarkdownChapters
{
    /// <summary>Every chapter of the file, in the order they were written. Empty
    /// for a file with no headings at all, which is a file with no chapters
    /// rather than one chapter with no name.</summary>
    public static IReadOnlyList<MarkdownChapter> Split(string? source)
    {
        if (string.IsNullOrEmpty(source)) return [];

        var lines = source.Replace("\r\n", "\n").Split('\n');
        var headings = Headings(lines);
        if (headings.Count == 0) return [];

        var chapters = new List<MarkdownChapter>(headings.Count);
        for (var i = 0; i < headings.Count; i++)
        {
            var (start, level, title) = headings[i];

            // The end is the next heading that is not beneath this one. A `###`
            // under a `##` belongs to it; the next `##` does not.
            var end = lines.Length;
            for (var next = i + 1; next < headings.Count; next++)
            {
                if (headings[next].Level > level) continue;

                end = headings[next].Line;
                break;
            }

            chapters.Add(new MarkdownChapter(level, title, Slice(lines, start, end)));
        }

        return chapters;
    }

    /// <summary>
    /// The heading lines, ignoring anything inside a fence.
    /// <para>
    /// The fence tracking is the whole reason this is not a regex over the file:
    /// a shell snippet's <c>#</c> comment and a diff's <c>### </c> marker are not
    /// chapters, and a file view that offered to copy one would copy from the
    /// wrong place onwards.
    /// </para>
    /// </summary>
    private static List<(int Line, int Level, string Title)> Headings(string[] lines)
    {
        var headings = new List<(int, int, string)>();
        char? fence = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart(' ');

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var marker = trimmed[0];

                // A fence is closed by its own character, so a ``` inside a ~~~
                // block stays content — which is how a markdown file quotes a
                // markdown fence, and this file's own chapters do exactly that.
                if (fence is null) fence = marker;
                else if (fence == marker) fence = null;

                continue;
            }

            if (fence is not null) continue;

            var level = 0;
            while (level < trimmed.Length && trimmed[level] == '#') level++;
            if (level is 0 or > 6) continue;

            // `#hashtag` is a tag and `#!/bin/sh` is a shebang; a heading needs
            // the space, which is what the parser asks for too.
            if (level >= trimmed.Length || trimmed[level] is not (' ' or '\t')) continue;

            var title = trimmed[level..].Trim();
            if (title.Length == 0) continue;

            headings.Add((index, level, title));
        }

        return headings;
    }

    /// <summary>The lines of one chapter, without the blank lines that only sat
    /// between it and whatever came next.</summary>
    private static string Slice(string[] lines, int start, int end)
    {
        var last = end - 1;
        while (last > start && lines[last].Trim().Length == 0) last--;

        return string.Join('\n', lines[start..(last + 1)]);
    }
}
