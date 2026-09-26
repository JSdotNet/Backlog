using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// Each chapter's slice of its file, twice: verbatim for <c>chapter.text</c> and
/// as prose for <c>chapter.search_text</c>, the column full-text search indexes.
///
/// <para>A port of <c>chapterSlices</c>, <c>fenceMask</c> and <c>proseText</c> in
/// <c>tools/devbook/build-database.mjs</c>; the reasons for every rule are
/// written there and in <c>devbook-schema.mjs</c>. Slices are disjoint — from a
/// heading to the line before the next heading of any level — and the first
/// starts at line 1 so a file's preamble stays in the corpus.</para>
/// </summary>
internal static partial class DevbookChapterText
{
    [GeneratedRegex("^ {0,3}(`{3,}|~{3,})(.*)$")]
    private static partial Regex Fence();

    [GeneratedRegex("^ {0,3}#{1,6}[" + DevbookMarkdown.JsWhitespaceClass + "]+")]
    private static partial Regex HeadingMarker();

    public static IReadOnlyList<(string Text, string SearchText)> Slices(string markdown, IReadOnlyList<DevbookParsedChapter> chapters)
    {
        var lines = DevbookMarkdown.Lines(markdown);
        var fenced = FenceMask(lines);
        var slices = new List<(string, string)>(chapters.Count);

        for (var index = 0; index < chapters.Count; index++)
        {
            var start = index == 0 ? 0 : chapters[index].Line - 1;
            var end = index + 1 < chapters.Count ? chapters[index + 1].Line - 1 : lines.Length;
            var text = end > start ? string.Join('\n', lines[start..end]) : string.Empty;
            slices.Add((text, ProseText(lines, fenced, start, end)));
        }

        return slices;
    }

    /// <summary>Whether each line is inside a fenced block, fences included,
    /// computed over the whole document.</summary>
    private static bool[] FenceMask(string[] lines)
    {
        var fenced = new bool[lines.Length];
        char? openCharacter = null;
        var openLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var match = Fence().Match(lines[index]);

            if (openCharacter is { } character)
            {
                fenced[index] = true;
                if (match.Success
                    && match.Groups[1].Value[0] == character
                    && match.Groups[1].Length >= openLength
                    && DevbookMarkdown.JsTrim(match.Groups[2].Value).Length == 0)
                {
                    openCharacter = null;
                }
            }
            else if (match.Success)
            {
                fenced[index] = true;
                openCharacter = match.Groups[1].Value[0];
                openLength = match.Groups[1].Length;
            }
        }

        return fenced;
    }

    private static string ProseText(string[] lines, bool[] fenced, int start, int end)
    {
        var kept = new List<string>();

        for (var index = start; index < end; index++)
        {
            if (fenced[index]) continue;

            var line = HeadingMarker().Replace(lines[index], string.Empty, 1);
            var blank = DevbookMarkdown.JsTrim(line).Length == 0;
            if (blank && (kept.Count == 0 || kept[^1].Length == 0)) continue;

            kept.Add(blank ? string.Empty : line);
        }

        while (kept.Count > 0 && kept[^1].Length == 0) kept.RemoveAt(kept.Count - 1);

        return string.Join('\n', kept);
    }
}
