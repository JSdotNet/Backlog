using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// The opening of a note's Markdown body as one line of prose, for the two lines
/// a row on the Notes tab shows under the title: the markers a person typed to
/// shape the note — headings, bullets, checkboxes, emphasis, link targets — are
/// not what they wrote, and a fenced block is code rather than prose. The row
/// clamps it to two lines; this only keeps it short enough to be worth clamping.
/// </summary>
public static partial class NoteSnippet
{
    /// <summary>Comfortably more than two lines on a phone.</summary>
    public const int MaximumLength = 160;

    /// <summary>
    /// The snippet under a row whose title was made from the body's first line —
    /// a note saved with no title of its own — without that line said twice.
    /// </summary>
    public static string Of(string? markdown, string? title)
    {
        var snippet = Of(markdown);
        var heading = title?.Trim();

        return !string.IsNullOrEmpty(heading) && snippet.StartsWith(heading, StringComparison.Ordinal)
            ? snippet[heading.Length..].TrimStart()
            : snippet;
    }

    public static string Of(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var text = new StringBuilder();
        var inFence = false;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence) continue;

            line = BlockMarker().Replace(line, string.Empty);
            line = Image().Replace(line, "$1");
            line = Link().Replace(line, "$1");
            line = Emphasis().Replace(line, string.Empty).Trim();

            if (line.Length == 0) continue;
            if (text.Length > 0) text.Append(' ');
            text.Append(line);

            if (text.Length > MaximumLength) break;
        }

        return Cut(text.ToString());
    }

    private static string Cut(string text)
    {
        if (text.Length <= MaximumLength) return text;

        var cut = text.LastIndexOf(' ', MaximumLength);
        return (cut > 0 ? text[..cut] : text[..MaximumLength]).TrimEnd() + "…";
    }

    /// <summary>A heading's hashes, a quote's bracket, a bullet or a number, and a
    /// checkbox after either.</summary>
    [GeneratedRegex(@"^(#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)*(\[[ xX]\]\s+)?")]
    private static partial Regex BlockMarker();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex Link();

    /// <summary>Emphasis, strike and code markers. An underscore only at a word's
    /// edge, so snake_case survives.</summary>
    [GeneratedRegex(@"\*+|~~|`+|(?<!\w)_+|_+(?!\w)")]
    private static partial Regex Emphasis();
}
