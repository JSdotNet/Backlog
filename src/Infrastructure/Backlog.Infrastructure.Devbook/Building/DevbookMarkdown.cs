using System.Text;
using System.Text.RegularExpressions;

using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Infrastructure.Devbook.Building;

/// <summary>
/// The devbook generator's Markdown parse, ported: headings, the <c>meta</c>
/// block under each, and the slug a heading is addressed by
/// (<see cref="HeadingAnchor"/>, shared with every other reader).
///
/// <para>A port of <c>parseDocument</c> and <c>parseMetaBody</c>
/// in the devbook plugin's <c>metadata.mjs</c>, which is what
/// <c>tools/devbook/build-database.mjs</c> imports. Local ADR 0015 made the app
/// the writer and accepted a second implementation of this parse; what keeps the
/// two agreeing is <c>DevbookBuilderParityTests</c>, which builds this
/// repository's own corpus both ways and compares the tables. So this file is
/// deliberately literal rather than idiomatic: where JavaScript and .NET disagree
/// on what "whitespace" or "trim" means, it takes JavaScript's answer, because
/// that is the side the rows are compared against.</para>
///
/// <para>It tracks fences when it looks for headings, as the generator does from
/// devbook 1.19, so a <c>#</c> line inside a diagram or a Markdown sample is
/// content and starts no chapter. The fence rule is the generator's, which is
/// looser than CommonMark's: any indentation, and a closing marker may carry
/// text after it.</para>
/// </summary>
internal static partial class DevbookMarkdown
{
    /// <summary>JavaScript's <c>\s</c> and <c>String.prototype.trim</c> set: the
    /// ECMAScript WhiteSpace and LineTerminator characters. It differs from .NET's
    /// in both directions — U+FEFF is in it, U+0085 is not.</summary>
    internal const string JsWhitespaceClass = EcmaScriptWhitespace.CharacterClass;

    [GeneratedRegex("\\r?\\n")]
    private static partial Regex LineBreak();

    /// <summary>JavaScript's <c>.</c> stops at every line terminator and .NET's
    /// only at <c>\n</c>, so the text is spelled out: a line holding a CR, U+2028
    /// or U+2029 is no heading to the generator. Group 1 is the markers, group 2
    /// the text as written.</summary>
    [GeneratedRegex("^(#{1,6})[" + JsWhitespaceClass + "]+([^\\n\\r\\u2028\\u2029]*)\\z")]
    internal static partial Regex Heading();

    /// <summary>The generator's fence marker: any indentation, and a closing
    /// marker may carry text after it.</summary>
    [GeneratedRegex("^[" + JsWhitespaceClass + "]*(`{3,}|~{3,})")]
    internal static partial Regex FenceMarker();

    [GeneratedRegex("[" + JsWhitespaceClass + "]+")]
    private static partial Regex JsWhitespaceRun();

    /// <summary>Splits a document into lines the way <c>split(/\r?\n/)</c> does.</summary>
    public static string[] Lines(string markdown) => LineBreak().Split(markdown);

    /// <summary>Whether a line opens the <c>meta</c> block the generator reads
    /// under a heading: the line, trimmed as JavaScript trims, is exactly
    /// <c>```meta</c> (<see cref="MetadataFence.IsGeneratorOpener"/>).</summary>
    internal static bool IsMetaFence(string line) => MetadataFence.IsGeneratorOpener(JsTrim(line));

    /// <summary>Every run of JavaScript whitespace in <paramref name="line"/> as one
    /// space, and the result trimmed: <c>replace(/\s+/g, ' ').trim()</c>.</summary>
    internal static string CollapseWhitespace(string line) => JsTrim(JsWhitespaceRun().Replace(line, " "));

    /// <summary>
    /// Every heading of every level, each with the <c>meta</c> block that follows
    /// it (past blank lines) when there is one. The first level-1 heading is also
    /// the file's title and its block the file's block. A field named in
    /// <paramref name="blockListFields"/> may also be a YAML block list
    /// (<see cref="DevbookCorpusProfile.BlockListFields"/>), read by
    /// <see cref="ReadBlockLists"/>.
    /// </summary>
    public static DevbookParsedDocument Parse(string markdown, IReadOnlyCollection<string>? blockListFields = null)
    {
        var lines = Lines(markdown);
        var chapters = new List<DevbookParsedChapter>();
        string? fileTitle = null;
        IReadOnlyDictionary<string, object?>? fileMeta = null;
        var fileTitleSeen = false;
        string? fence = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var marker = FenceMarker().Match(lines[i]);
            if (fence is not null)
            {
                var closer = marker.Groups[1].Value;
                if (marker.Success && closer[0] == fence[0] && closer.Length >= fence.Length) fence = null;
                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups[1].Value;
                continue;
            }

            var heading = Heading().Match(lines[i]);
            if (!heading.Success) continue;

            var level = heading.Groups[1].Length;
            var text = JsTrim(heading.Groups[2].Value);
            var slug = HeadingAnchor.Of(text);

            var j = i + 1;
            while (j < lines.Length && JsTrim(lines[j]).Length == 0) j++;

            Dictionary<string, object?>? meta = null;
            if (j < lines.Length && IsMetaFence(lines[j]))
            {
                var body = new List<string>();
                var k = j + 1;
                while (k < lines.Length && JsTrim(lines[k]) != "```")
                {
                    body.Add(lines[k]);
                    k++;
                }

                meta = ParseMetaBody(body);
                if (blockListFields is { Count: > 0 }) ReadBlockLists(body, blockListFields, meta);
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
    internal static Dictionary<string, object?> ParseMetaBody(IEnumerable<string> lines)
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

    /// <summary>
    /// <c>readBlockLists</c> in <c>tools/devbook/brain.mjs</c>: each of
    /// <paramref name="fields"/> whose last line in the block has no value takes
    /// the <c>- item</c> lines under it — blank lines and <c># comments</c>
    /// between them kept out, any other line ending the list — trimmed and
    /// unquoted, as a flow list's entries are. A field whose last line has a value
    /// keeps the parse's reading, as a later key wins; one with no items stays
    /// null.
    /// </summary>
    private static void ReadBlockLists(IEnumerable<string> lines, IReadOnlyCollection<string> fields, Dictionary<string, object?> meta)
    {
        var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? open = null;

        foreach (var line in lines)
        {
            var trimmed = JsTrim(line);
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            if (trimmed[0] == '-' && (trimmed.Length == 1 || IsJsWhitespace(trimmed[1])))
            {
                var item = StripQuotes(JsTrim(trimmed[1..]));
                if (open is not null && item.Length > 0) open.Add(item);
                continue;
            }

            open = null;
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon == -1) continue;

            var key = JsTrim(line[..colon]);
            if (!fields.Contains(key)) continue;

            if (JsTrim(line[(colon + 1)..]).Length == 0) lists[key] = open = new List<string>();
            else lists.Remove(key);
        }

        foreach (var (key, items) in lists)
        {
            if (items.Count > 0) meta[key] = items.ToArray();
        }
    }

    private static object? ParseScalar(string raw)
    {
        var value = JsTrim(raw);
        if (value is "null" or "") return null;

        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var inner = JsTrim(value[1..^1]);
            if (inner.Length == 0) return Array.Empty<string>();

            return SplitListEntries(inner)
                .Select(entry => StripQuotes(JsTrim(entry)))
                .Where(entry => entry.Length > 0)
                .ToArray();
        }

        return StripQuotes(value);
    }

    /// <summary>
    /// <c>splitListEntries</c>: a list split on its commas, except a comma inside
    /// a quoted entry, which is part of it — a demo address carries
    /// <c>flags=a,b</c>. A quote opens only at an entry's start, past whitespace.
    /// </summary>
    private static List<string> SplitListEntries(string inner)
    {
        var entries = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        foreach (var character in inner)
        {
            if (quote is { } open)
            {
                if (character == open) quote = null;
            }
            else if (character is '"' or '\'')
            {
                if (JsTrim(current.ToString()).Length == 0) quote = character;
            }
            else if (character == ',')
            {
                entries.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        entries.Add(current.ToString());
        return entries;
    }

    private static string StripQuotes(string value) =>
        value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;

    /// <summary><c>String.prototype.trim</c>.</summary>
    public static string JsTrim(string value) => EcmaScriptWhitespace.Trim(value);

    internal static bool IsJsWhitespace(char c) => EcmaScriptWhitespace.Is(c);

    /// <summary>A metadata value as a list, the way <c>asList</c> reads it.</summary>
    public static IReadOnlyList<string> AsList(object? value) =>
        value switch
        {
            null => [],
            IReadOnlyList<string> list => list,
            string single => [single],
            _ => [Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty]
        };

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
