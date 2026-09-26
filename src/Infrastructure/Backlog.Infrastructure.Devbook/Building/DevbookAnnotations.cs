using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The open review notes of one document, by the chapter row that carries them —
/// what <c>chapter.open_annotations</c> holds.
///
/// <para>A port of as much of the devbook generator as that one number needs:
/// <c>parseAnnotations</c>, <c>parseAnnotationBody</c> and <c>resolveAnnotation</c>
/// in <c>metadata.mjs</c>, <c>openCountsByAddress</c> in
/// <c>annotations-index.mjs</c>, and <c>openCountsForFile</c> in
/// <c>tools/devbook/build-database.mjs</c>. A note is an <c>annotation</c> fence;
/// it is open when its <c>status</c> resolves to <c>open</c>, which is the default
/// when it states none. Its address is <c>&lt;path&gt;#&lt;slug&gt;</c> of the
/// nearest heading above it — found with fences skipped, unlike the chapter
/// parse — or the bare path above the first heading. An address counts on the row
/// with that slug and the smallest line; a bare path on the file's first row; an
/// address that names no row is counted nowhere.</para>
/// </summary>
internal static partial class DevbookAnnotations
{
    private const string OpenStatus = "open";

    /// <summary>
    /// The open-note count for each of <paramref name="chapters"/> — one file's
    /// rows, in document order — as a parallel array.
    /// </summary>
    public static int[] OpenCounts(string markdown, IReadOnlyList<DevbookParsedChapter> chapters)
    {
        var counts = new int[chapters.Count];
        if (chapters.Count == 0) return counts;

        var open = OpenBySlug(markdown);
        if (open.Count == 0) return counts;

        var first = 0;
        var firstBySlug = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            if (!firstBySlug.TryGetValue(chapter.Slug, out var current) || chapter.Line < chapters[current].Line)
            {
                firstBySlug[chapter.Slug] = index;
            }

            if (chapter.Line < chapters[first].Line) first = index;
        }

        foreach (var (slug, count) in open)
        {
            // A note above the first heading has no chapter: the generator
            // addresses it by the bare path, and the file's first row carries it.
            if (slug is null) counts[first] += count;
            else if (firstBySlug.TryGetValue(slug, out var index)) counts[index] += count;
        }

        return counts;
    }

    /// <summary>Open notes by the slug of the chapter they sit in, the key
    /// <see langword="null"/> for none — the generator's address, less the path.
    /// An empty slug addresses the bare path too, since it is falsy there.</summary>
    private static List<(string? Slug, int Count)> OpenBySlug(string markdown)
    {
        var lines = DevbookMarkdown.Lines(markdown);
        var counts = new List<(string? Slug, int Count)>();
        string? slug = null;

        for (var i = 0; i < lines.Length;)
        {
            var heading = Heading().Match(lines[i]);
            if (heading.Success)
            {
                slug = DevbookMarkdown.Slugify(DevbookMarkdown.JsTrim(heading.Groups[2].Value));
                i++;
                continue;
            }

            var fence = Fence().Match(lines[i]);
            if (!fence.Success)
            {
                // Prose, a blank line: neither moves the chapter a note belongs to.
                i++;
                continue;
            }

            var marker = fence.Groups[2].Value;
            var closer = new Regex(
                $"^[{DevbookMarkdown.JsWhitespaceClass}]*{Regex.Escape(marker[0].ToString())}{{{marker.Length},}}[{DevbookMarkdown.JsWhitespaceClass}]*\\z");

            var body = new List<string>();
            var k = i + 1;
            while (k < lines.Length && !closer.IsMatch(lines[k]))
            {
                body.Add(lines[k]);
                k++;
            }

            if (string.Equals(fence.Groups[3].Value.ToLowerInvariant(), "annotation", StringComparison.Ordinal)
                && IsOpen(string.Join('\n', body)))
            {
                var key = string.IsNullOrEmpty(slug) ? null : slug;
                var at = counts.FindIndex(entry => entry.Slug == key);
                if (at < 0) counts.Add((key, 1));
                else counts[at] = (key, counts[at].Count + 1);
            }

            i = k + 1;
        }

        return counts;
    }

    /// <summary><c>resolveAnnotation(parseAnnotationBody(raw)).status === "open"</c>:
    /// the top-level <c>status</c> field, <c>open</c> when absent or empty.</summary>
    private static bool IsOpen(string body)
    {
        var lines = body.Split('\n');
        var first = 0;
        while (first < lines.Length && IsBlank(lines[first])) first++;
        if (first >= lines.Length) return true;

        var (map, _) = ParseMapping(lines, first, lines.Length, Indent(lines[first]));
        return map.GetValueOrDefault("status") is not { } status || status is string { } text && text == OpenStatus;
    }

    private static (Dictionary<string, object?> Map, int Next) ParseMapping(string[] lines, int start, int end, int indent)
    {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var i = start;
        while (i < end)
        {
            if (IsBlank(lines[i]))
            {
                i++;
                continue;
            }

            if (Indent(lines[i]) < indent) break;

            var match = Key().Match(lines[i][Math.Min(indent, lines[i].Length)..]);
            if (!match.Success)
            {
                i++;
                continue;
            }

            var key = DevbookMarkdown.JsTrim(match.Groups[1].Value);
            var rest = DevbookMarkdown.JsTrim(match.Groups[2].Value);
            if (rest is "|" or "|-" or ">")
            {
                var (text, next) = ParseBlockScalar(lines, i + 1, end, indent);
                map[key] = text;
                i = next;
            }
            else if (rest.Length == 0)
            {
                var (child, next) = ParseNested(lines, i + 1, end, indent);
                map[key] = child;
                i = next;
            }
            else
            {
                map[key] = StripQuotes(rest);
                i++;
            }
        }

        return (map, i);
    }

    private static (string Text, int Next) ParseBlockScalar(string[] lines, int start, int end, int parentIndent)
    {
        var collected = new List<string>();
        var i = start;
        int? blockIndent = null;
        while (i < end)
        {
            if (IsBlank(lines[i]))
            {
                collected.Add(string.Empty);
                i++;
                continue;
            }

            var indent = Indent(lines[i]);
            if (indent <= parentIndent) break;
            blockIndent ??= indent;
            collected.Add(lines[i][Math.Min(blockIndent.Value, indent)..]);
            i++;
        }

        while (collected.Count > 0 && collected[^1].Length == 0) collected.RemoveAt(collected.Count - 1);
        return (string.Join('\n', collected), i);
    }

    private static (object? Value, int Next) ParseNested(string[] lines, int start, int end, int parentIndent)
    {
        var i = start;
        while (i < end && IsBlank(lines[i])) i++;
        if (i >= end || Indent(lines[i]) <= parentIndent) return (null, i);

        var childIndent = Indent(lines[i]);
        if (ListItem().IsMatch(lines[i][childIndent..]))
        {
            var (items, next) = ParseList(lines, i, end, childIndent);
            return (items, next);
        }

        var (map, after) = ParseMapping(lines, i, end, childIndent);
        return (map, after);
    }

    private static (List<object?> Items, int Next) ParseList(string[] lines, int start, int end, int indent)
    {
        var items = new List<object?>();
        var i = start;
        while (i < end)
        {
            if (IsBlank(lines[i]))
            {
                i++;
                continue;
            }

            if (Indent(lines[i]) < indent) break;
            if (!ListItem().IsMatch(lines[i][indent..])) break;

            var itemLines = new List<string> { new string(' ', indent + 2) + Slice(lines[i], indent + 2) };
            var j = i + 1;
            while (j < end)
            {
                if (IsBlank(lines[j]))
                {
                    itemLines.Add(lines[j]);
                    j++;
                    continue;
                }

                if (Indent(lines[j]) <= indent) break;
                itemLines.Add(lines[j]);
                j++;
            }

            while (itemLines.Count > 0 && IsBlank(itemLines[^1])) itemLines.RemoveAt(itemLines.Count - 1);

            var head = Slice(itemLines[0], indent + 2);
            if (KeyStart().IsMatch(head))
            {
                var (value, _) = ParseMapping([.. itemLines], 0, itemLines.Count, indent + 2);
                items.Add(value);
            }
            else
            {
                items.Add(StripQuotes(DevbookMarkdown.JsTrim(head)));
            }

            i = j;
        }

        return (items, i);
    }

    /// <summary><c>String.prototype.slice</c> from <paramref name="start"/>, which
    /// is empty rather than an error past the end.</summary>
    private static string Slice(string value, int start) => start >= value.Length ? string.Empty : value[start..];

    private static int Indent(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ') count++;
        return count;
    }

    private static bool IsBlank(string line) => DevbookMarkdown.JsTrim(line).Length == 0;

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    // `/^(#{1,6})\s+(.*)$/`: `.` stops at a line terminator in JavaScript, so a
    // heading line holding a stray `\r` does not match there either.
    [GeneratedRegex("^(#{1,6})[" + DevbookMarkdown.JsWhitespaceClass + "]+([^\\n\\r\\u2028\\u2029]*)\\z")]
    private static partial Regex Heading();

    // FENCE_PATTERN: `/^(\s*)(`{3,}|~{3,})\s*([^\s`~]*)\s*$/`.
    [GeneratedRegex("^([" + DevbookMarkdown.JsWhitespaceClass + "]*)(`{3,}|~{3,})[" + DevbookMarkdown.JsWhitespaceClass + "]*([^" + DevbookMarkdown.JsWhitespaceClass + "`~]*)[" + DevbookMarkdown.JsWhitespaceClass + "]*\\z")]
    private static partial Regex Fence();

    // `/^([^:\s][^:]*):[ \t]?(.*)$/`.
    [GeneratedRegex("^([^:" + DevbookMarkdown.JsWhitespaceClass + "][^:]*):[ \\t]?([^\\n\\r\\u2028\\u2029]*)\\z")]
    private static partial Regex Key();

    [GeneratedRegex("^[^:" + DevbookMarkdown.JsWhitespaceClass + "][^:]*:")]
    private static partial Regex KeyStart();

    [GeneratedRegex("^-[" + DevbookMarkdown.JsWhitespaceClass + "]")]
    private static partial Regex ListItem();
}
