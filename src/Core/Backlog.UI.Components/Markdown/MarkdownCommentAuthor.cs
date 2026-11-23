using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// How a remark's author reads in the view.
/// <para>
/// A remark a tool left carries the tool's slug — <c>claude/chapter-review</c> — and
/// a slug is an address rather than a name, so it reads as one:
/// <c>Claude · chapter review</c>. Every other author is what a person or a host
/// wrote, and stays exactly that. The rule lives in the view rather than in the
/// host because only the view sets both the friendly text and the raw value on
/// its <c>title</c>, so a reader can still see who exactly left it.
/// </para>
/// </summary>
internal static partial class MarkdownCommentAuthor
{
    private const string Separator = " · ";

    /// <summary>Whether an author is shaped like a slug: no whitespace, at least
    /// one <c>/</c>, and every segment non-empty and made of
    /// <c>[A-Za-z0-9._-]</c>. Anything else — a machine name, an email, a phrase —
    /// is somebody's own wording.</summary>
    public static bool IsSlug(string? author) =>
        !string.IsNullOrEmpty(author) && SlugPattern().IsMatch(author);

    /// <summary>The author as the view shows it: a slug with its first segment
    /// capitalised, the later ones with <c>-</c> and <c>_</c> read as spaces, and
    /// the segments joined by a middle dot. Any other value unchanged.</summary>
    public static string Display(string author)
    {
        ArgumentNullException.ThrowIfNull(author);

        if (!IsSlug(author))
        {
            return author;
        }

        var segments = author.Split('/');
        var first = segments[0];
        var parts = new string[segments.Length];
        parts[0] = char.ToUpperInvariant(first[0]) + first[1..];

        for (var i = 1; i < segments.Length; i++)
        {
            parts[i] = segments[i].Replace('-', ' ').Replace('_', ' ');
        }

        return string.Join(Separator, parts);
    }

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
