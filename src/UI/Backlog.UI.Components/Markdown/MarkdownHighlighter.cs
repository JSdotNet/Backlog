using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Markdown;

/// <summary>What a run of characters is doing in a markdown source.</summary>
public enum MarkdownSyntaxKind
{
    Plain,

    /// <summary>The characters that are syntax rather than content — the dash on
    /// a bullet, the pipes in a table, the fence itself.</summary>
    Marker,

    /// <summary>A heading's own hashes. Kept apart from <see cref="Marker"/>
    /// because they are how a reader sees, scrolling past, how deep a section is
    /// — that is worth more than one more grey run.</summary>
    HeadingMarker,

    Heading,
    Strong,
    Emphasis,
    Strike,
    Code,
    Quote,
    LinkText,
    Url,
    Tag
}

public sealed record MarkdownSyntaxToken(MarkdownSyntaxKind Kind, string Text)
{
    /// <summary>The class the editor's highlight layer puts on this run. Asked of
    /// the token rather than written out by the caller, so a legend cannot drift
    /// from what is actually rendered — the same bargain CodeToken makes.</summary>
    public string CssClass => Kind switch
    {
        MarkdownSyntaxKind.Marker => "md-syntax--marker",
        MarkdownSyntaxKind.HeadingMarker => "md-syntax--heading-marker",
        MarkdownSyntaxKind.Heading => "md-syntax--heading",
        MarkdownSyntaxKind.Strong => "md-syntax--strong",
        MarkdownSyntaxKind.Emphasis => "md-syntax--emphasis",
        MarkdownSyntaxKind.Strike => "md-syntax--strike",
        MarkdownSyntaxKind.Code => "md-syntax--code",
        MarkdownSyntaxKind.Quote => "md-syntax--quote",
        MarkdownSyntaxKind.LinkText => "md-syntax--link",
        MarkdownSyntaxKind.Url => "md-syntax--url",
        MarkdownSyntaxKind.Tag => "md-syntax--tag",
        _ => "md-syntax--plain"
    };
}

public sealed record MarkdownSyntaxLine(IReadOnlyList<MarkdownSyntaxToken> Tokens);

/// <summary>
/// Colours markdown <em>as source</em>, for the editor to draw behind the text
/// being typed.
/// <para>
/// This is not <see cref="MarkdownPreview"/> and deliberately does not share its
/// code. The parser answers "what does this mean", throws the syntax away and
/// hands back a tree; a highlighter answers "what is each character doing" and
/// must account for every character including the markers — an editor that lost
/// a backtick when it coloured one would be an editor that ate your text.
/// </para>
/// <para>
/// It is also allowed to be more forgiving. A half-typed <c>**bold</c> is a
/// thing someone is in the middle of writing, and colouring it as plain until it
/// closes is the right answer rather than a failure.
/// </para>
/// </summary>
public static class MarkdownHighlighter
{
    private static readonly Regex HeadingRegex = new(@"^(\s*#{1,6}\s+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex QuoteRegex = new(@"^(\s*>\s?)(.*)$", RegexOptions.Compiled);
    private static readonly Regex TaskRegex = new(@"^(\s*[-*]\s+\[[ xX]\]\s+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^(\s*[-*]\s+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^(\s*\d+[.)]\s+)(.*)$", RegexOptions.Compiled);
    private static readonly Regex DividerRegex = new(@"^\s*(-{3,}|\*{3,}|_{3,})\s*$", RegexOptions.Compiled);
    private static readonly Regex TableRowRegex = new(@"^\s*\|.*$", RegexOptions.Compiled);

    /// <summary>The inline forms, markers included. Every alternative captures
    /// its whole span so nothing between the markers is lost on the way out.</summary>
    private static readonly Regex InlineRegex = new(
        @"(?<code>`[^`\n]+`)" +
        @"|(?<image>!\[[^\]\n]*\]\([^)\n]*\))" +
        @"|(?<link>\[[^\]\n]+\]\([^)\n]*\))" +
        @"|(?<strong>\*\*[^*\n]+\*\*)" +
        @"|(?<strike>~~[^~\n]+~~)" +
        @"|(?<em>(?<!\*)\*[^*\n]+\*(?!\*))" +
        @"|(?<tag>(?<!\S)#[A-Za-z][\w-]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// One entry per line of the source, in order, including the blank ones — the
    /// layer is drawn behind a textarea and has to have exactly as many lines as
    /// the text does or the colours slide off the words.
    /// </summary>
    public static IReadOnlyList<MarkdownSyntaxLine> Highlight(string? source)
    {
        var text = (source ?? string.Empty).Replace("\r\n", "\n");
        var lines = text.Split('\n');
        var result = new List<MarkdownSyntaxLine>(lines.Length);
        var inFence = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                result.Add(One(MarkdownSyntaxKind.Marker, line));
                continue;
            }

            // Inside a fence nothing is markdown, which is the whole point of a
            // fence and the reason the editor must not colour it as any.
            if (inFence)
            {
                result.Add(One(MarkdownSyntaxKind.Code, line));
                continue;
            }

            result.Add(new MarkdownSyntaxLine(ReadLine(line)));
        }

        return result;
    }

    private static MarkdownSyntaxLine One(MarkdownSyntaxKind kind, string text) =>
        new(text.Length == 0 ? [] : [new MarkdownSyntaxToken(kind, text)]);

    private static IReadOnlyList<MarkdownSyntaxToken> ReadLine(string line)
    {
        if (line.Length == 0) return [];

        if (DividerRegex.IsMatch(line)) return [new(MarkdownSyntaxKind.Marker, line)];

        // A table row is pipes and content; the pipes are the syntax.
        if (TableRowRegex.IsMatch(line)) return ReadTableRow(line);

        var heading = HeadingRegex.Match(line);
        if (heading.Success)
        {
            // The whole heading is the heading, hashes and all: it is one thing
            // on the page and reads as one thing here.
            return [new(MarkdownSyntaxKind.HeadingMarker, heading.Groups[1].Value), .. Inlines(heading.Groups[2].Value, MarkdownSyntaxKind.Heading)];
        }

        var quote = QuoteRegex.Match(line);
        if (quote.Success)
        {
            return [new(MarkdownSyntaxKind.Marker, quote.Groups[1].Value), .. Inlines(quote.Groups[2].Value, MarkdownSyntaxKind.Quote)];
        }

        // Task before bullet: a task line is also a bullet line, and matching the
        // bullet first would colour the checkbox as content.
        var prefixed = TaskRegex.Match(line);
        prefixed = prefixed.Success ? prefixed : BulletRegex.Match(line);
        prefixed = prefixed.Success ? prefixed : OrderedRegex.Match(line);

        return prefixed.Success
            ? [new(MarkdownSyntaxKind.Marker, prefixed.Groups[1].Value), .. Inlines(prefixed.Groups[2].Value, MarkdownSyntaxKind.Plain)]
            : Inlines(line, MarkdownSyntaxKind.Plain);
    }

    private static IReadOnlyList<MarkdownSyntaxToken> ReadTableRow(string line)
    {
        var tokens = new List<MarkdownSyntaxToken>();
        var cell = new System.Text.StringBuilder();

        void FlushCell()
        {
            if (cell.Length == 0) return;
            tokens.AddRange(Inlines(cell.ToString(), MarkdownSyntaxKind.Plain));
            cell.Clear();
        }

        foreach (var ch in line)
        {
            if (ch == '|')
            {
                FlushCell();
                tokens.Add(new(MarkdownSyntaxKind.Marker, "|"));
                continue;
            }

            cell.Append(ch);
        }

        FlushCell();
        return tokens;
    }

    /// <summary>The inline pass over the content of one line. Anything that
    /// matches nothing keeps <paramref name="fallback"/>, which is how a heading
    /// stays a heading through the words that are only words.</summary>
    private static IReadOnlyList<MarkdownSyntaxToken> Inlines(string text, MarkdownSyntaxKind fallback)
    {
        if (text.Length == 0) return [];

        var tokens = new List<MarkdownSyntaxToken>();
        var cursor = 0;

        foreach (Match match in InlineRegex.Matches(text))
        {
            if (match.Index > cursor) tokens.Add(new(fallback, text[cursor..match.Index]));

            var value = match.Value;

            if (match.Groups["code"].Success) tokens.Add(new(MarkdownSyntaxKind.Code, value));
            else if (match.Groups["strong"].Success) tokens.Add(new(MarkdownSyntaxKind.Strong, value));
            else if (match.Groups["strike"].Success) tokens.Add(new(MarkdownSyntaxKind.Strike, value));
            else if (match.Groups["em"].Success) tokens.Add(new(MarkdownSyntaxKind.Emphasis, value));
            else if (match.Groups["tag"].Success) tokens.Add(new(MarkdownSyntaxKind.Tag, value));
            else if (match.Groups["link"].Success || match.Groups["image"].Success)
            {
                // The text and the URL are coloured apart, because they are read
                // apart: one is prose, the other is a machine string.
                var split = value.IndexOf("](", StringComparison.Ordinal);
                tokens.Add(new(MarkdownSyntaxKind.LinkText, value[..(split + 1)]));
                tokens.Add(new(MarkdownSyntaxKind.Url, value[(split + 1)..]));
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) tokens.Add(new(fallback, text[cursor..]));

        return tokens;
    }
}
