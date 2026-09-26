using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The devbook generator's Markdown parse, ported: headings, the <c>meta</c>
/// block under each, and the slug a heading is addressed by.
///
/// <para>A port of <c>parseDocument</c>, <c>parseMetaBody</c> and <c>slugify</c>
/// in the devbook plugin's <c>metadata.mjs</c>, which is what
/// <c>tools/devbook/build-database.mjs</c> imports. Local ADR 0015 made the app
/// the writer and accepted a second implementation of this parse; what keeps the
/// two agreeing is <c>DevbookBuilderParityTests</c>, which builds this
/// repository's own corpus both ways and compares the tables. So this file is
/// deliberately literal rather than idiomatic: where JavaScript and .NET disagree
/// on what "whitespace" or "trim" means, it takes JavaScript's answer, because
/// that is the side the rows are compared against.</para>
///
/// <para>It does not track fences when it looks for headings — neither does the
/// generator — so a <c>#</c> line inside a diagram starts a chapter here exactly
/// as it does there.</para>
/// </summary>
internal static partial class DevbookMarkdown
{
    /// <summary>JavaScript's <c>\s</c> and <c>String.prototype.trim</c> set: the
    /// ECMAScript WhiteSpace and LineTerminator characters. It differs from .NET's
    /// in both directions — U+FEFF is in it, U+0085 is not.</summary>
    internal const string JsWhitespaceClass = "\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff";

    [GeneratedRegex("\\r?\\n")]
    private static partial Regex LineBreak();

    [GeneratedRegex("^(#{1,6})[" + JsWhitespaceClass + "]+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex("^```meta[" + JsWhitespaceClass + "]*$")]
    private static partial Regex MetaFence();

    [GeneratedRegex("[^\\p{L}\\p{N}_" + JsWhitespaceClass + "-]")]
    private static partial Regex NotSlugCharacter();

    [GeneratedRegex("[" + JsWhitespaceClass + "]")]
    private static partial Regex JsWhitespace();

    /// <summary>Splits a document into lines the way <c>split(/\r?\n/)</c> does.</summary>
    public static string[] Lines(string markdown) => LineBreak().Split(markdown);

    /// <summary>
    /// Every heading of every level, each with the <c>meta</c> block that follows
    /// it (past blank lines) when there is one. The first level-1 heading is also
    /// the file's title and its block the file's block.
    /// </summary>
    public static DevbookParsedDocument Parse(string markdown)
    {
        var lines = Lines(markdown);
        var chapters = new List<DevbookParsedChapter>();
        string? fileTitle = null;
        IReadOnlyDictionary<string, object?>? fileMeta = null;
        var fileTitleSeen = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var heading = Heading().Match(lines[i]);
            if (!heading.Success) continue;

            var level = heading.Groups[1].Length;
            var text = JsTrim(heading.Groups[2].Value);
            var slug = Slugify(text);

            var j = i + 1;
            while (j < lines.Length && JsTrim(lines[j]).Length == 0) j++;

            Dictionary<string, object?>? meta = null;
            if (j < lines.Length && MetaFence().IsMatch(JsTrim(lines[j])))
            {
                var body = new List<string>();
                var k = j + 1;
                while (k < lines.Length && JsTrim(lines[k]) != "```")
                {
                    body.Add(lines[k]);
                    k++;
                }

                meta = ParseMetaBody(body);
            }

            chapters.Add(new DevbookParsedChapter(level, text, slug, i + 1, meta));

            if (level == 1 && !fileTitleSeen)
            {
                fileTitleSeen = true;
                fileTitle = text;
                fileMeta = meta;
            }
        }

        return new DevbookParsedDocument(fileTitle, fileMeta, chapters);
    }

    /// <summary>
    /// One <c>key: value</c> per line; a value is <see langword="null"/>, a
    /// string, or — written <c>[a, b]</c> — a list of strings. A later key wins.
    /// </summary>
    private static Dictionary<string, object?> ParseMetaBody(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (JsTrim(line).Length == 0) continue;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon == -1) continue;

            result[JsTrim(line[..colon])] = ParseScalar(line[(colon + 1)..]);
        }

        return result;
    }

    private static object? ParseScalar(string raw)
    {
        var value = JsTrim(raw);
        if (value is "null" or "") return null;

        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var inner = JsTrim(value[1..^1]);
            if (inner.Length == 0) return Array.Empty<string>();

            return inner
                .Split(',')
                .Select(entry => StripQuotes(JsTrim(entry)))
                .Where(entry => entry.Length > 0)
                .ToArray();
        }

        return StripQuotes(value);
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary>
    /// GitHub's anchor rule as the generator spells it: lower-case, trim, drop
    /// everything but letters, digits, underscores, whitespace and hyphens, then
    /// turn each remaining whitespace character into a hyphen — without
    /// collapsing runs.
    /// </summary>
    public static string Slugify(string text)
    {
        var lowered = JsTrim(text.ToLowerInvariant());
        var kept = NotSlugCharacter().Replace(lowered, string.Empty);
        return JsWhitespace().Replace(kept, "-");
    }

    /// <summary><c>String.prototype.trim</c>.</summary>
    public static string JsTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhitespace(value[start])) start++;
        while (end > start && IsJsWhitespace(value[end - 1])) end--;
        return start == 0 && end == value.Length ? value : value[start..end];
    }

    private static bool IsJsWhitespace(char c) =>
        (int)c is 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x20 or 0xa0 or 0x1680
            or (>= 0x2000 and <= 0x200a)
            or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 or 0xfeff;

    /// <summary>A metadata value as a list, the way <c>asList</c> reads it.</summary>
    public static IReadOnlyList<string> AsList(object? value) =>
        value switch
        {
            null => [],
            IReadOnlyList<string> list => list,
            string single => [single],
            _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty]
        };

    /// <summary>A metadata value as the single string a column holds, or
    /// <see langword="null"/> for a list: a list in a scalar column is a
    /// malformed block, and the checker is what reports it.</summary>
    public static string? AsScalar(object? value) => value as string;

    /// <summary>Lower-case hexadecimal SHA-256 of <paramref name="text"/>'s UTF-8
    /// bytes, as <c>createHash('sha256').update(text, 'utf8')</c> gives it.</summary>
    public static string Sha256(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>A parsed devbook document: its title, its file-level block, and
/// every heading in order.</summary>
internal sealed record DevbookParsedDocument(
    string? FileTitle,
    IReadOnlyDictionary<string, object?>? FileMeta,
    IReadOnlyList<DevbookParsedChapter> Chapters);

/// <summary>One heading. <see cref="Line"/> is 1-based; <see cref="Meta"/> is
/// <see langword="null"/> for a structural heading with no block.</summary>
internal sealed record DevbookParsedChapter(
    int Level,
    string Text,
    string Slug,
    int Line,
    IReadOnlyDictionary<string, object?>? Meta);
