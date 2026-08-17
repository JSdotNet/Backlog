using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// The inline half of the read view — emphasis, code spans, <c>#tags</c> and
/// links. It lives here rather than inside <c>MarkdownView</c> because a
/// sub-item's title is rendered outside the block tree now that sub-items are
/// cards of their own, and both places must read the same.
/// <para>
/// Everything is emitted through the render tree's normal escaping, so nothing
/// typed into an entry can inject markup, and link URLs are filtered by scheme
/// (see <see cref="IsNavigable"/>) so nothing typed into one can inject
/// behaviour either.
/// </para>
/// </summary>
public static class MarkdownRender
{
    /// <summary>
    /// The inlines as flat text, for the places that need a label rather than a
    /// rendering — an <c>aria-label</c> that has to name the item it acts on.
    /// </summary>
    public static string PlainText(IReadOnlyList<MdInline> parts) => string.Concat(parts.Select(part => part switch
    {
        MdText text => text.Text,
        MdStrong strong => strong.Text,
        MdEm em => em.Text,
        MdStrike strike => strike.Text,
        MdCodeSpan codeSpan => codeSpan.Text,
        MdTag tag => $"#{tag.Tag}",
        MdLink link => link.Text,
        // The alt text is the whole of what an image says to something that
        // cannot see it, which is exactly what this is for.
        MdImage image => image.Alt,
        // A label that names an item in a list somewhere below is noise in a
        // label; the mark itself carries nothing a reader needs here.
        MdFootnoteRef => string.Empty,
        _ => string.Empty
    }));

    public static RenderFragment Inlines(IReadOnlyList<MdInline> parts) => builder =>
    {
        var seq = 0;
        foreach (var part in parts)
        {
            switch (part)
            {
                case MdText text:
                    builder.AddContent(seq++, text.Text);
                    break;
                case MdStrong strong:
                    Wrap(builder, ref seq, "strong", null, strong.Text);
                    break;
                case MdEm em:
                    Wrap(builder, ref seq, "em", null, em.Text);
                    break;
                case MdStrike strike:
                    Wrap(builder, ref seq, "s", null, strike.Text);
                    break;
                case MdCodeSpan codeSpan:
                    Wrap(builder, ref seq, "code", "md-inline-code", codeSpan.Text);
                    break;
                case MdTag tag:
                    Wrap(builder, ref seq, "span", "tag-chip", $"#{tag.Tag}");
                    break;
                case MdLink link when IsNavigable(link.Url):
                    builder.OpenElement(seq++, "a");
                    builder.AddAttribute(seq++, "class", "md-link");
                    builder.AddAttribute(seq++, "href", link.Url);
                    builder.AddAttribute(seq++, "target", "_blank");
                    builder.AddAttribute(seq++, "rel", "noopener noreferrer");
                    builder.AddContent(seq++, link.Text);
                    builder.CloseElement();
                    break;
                case MdLink link:
                    // Not a scheme we will navigate to. The text stays — the
                    // author wrote it — but it is not clickable.
                    Wrap(builder, ref seq, "span", "md-link--inert", link.Text);
                    break;

                case MdImage image when IsNavigable(image.Url):
                    builder.OpenElement(seq++, "img");
                    builder.AddAttribute(seq++, "class", "md-image");
                    builder.AddAttribute(seq++, "src", image.Url);
                    builder.AddAttribute(seq++, "alt", image.Alt);
                    // Nothing here knows how big the picture is, and a body that
                    // reflows when one arrives is worse than one that waits.
                    builder.AddAttribute(seq++, "loading", "lazy");
                    builder.CloseElement();
                    break;

                case MdImage image:
                    // Same allow-list as a link, and for a stronger reason: a
                    // `src` fetches on its own, with no click to consent to it.
                    // The alt text is what the author meant, so it is what shows.
                    Wrap(builder, ref seq, "span", "md-image--inert", image.Alt);
                    break;

                case MdFootnoteRef reference:
                    // A real anchor, so the mark is reachable and announced, and
                    // the note it points at is one keystroke away.
                    builder.OpenElement(seq++, "sup");
                    builder.AddAttribute(seq++, "class", "md-fnref");
                    builder.AddAttribute(seq++, "id", $"fnref-{reference.Label}");
                    builder.OpenElement(seq++, "a");
                    builder.AddAttribute(seq++, "href", $"#fn-{reference.Label}");
                    builder.AddAttribute(seq++, "aria-label", $"Footnote {reference.Number}");
                    builder.AddContent(seq++, reference.Number);
                    builder.CloseElement();
                    builder.CloseElement();
                    break;
            }
        }
    };

    /// <summary>
    /// Whether a link URL is one we are willing to put in an <c>href</c>.
    /// Escaping keeps typed markup out of the document, but an <c>href</c> is a
    /// second door: <c>javascript:</c> and <c>data:</c> URLs run in the app's own
    /// origin under WebView2 (ADR 0001), and an entry body can arrive from a
    /// repository file nobody here wrote. So the scheme is an allow-list, not a
    /// block-list — anything unrecognised is text, not a link.
    /// </summary>
    private static bool IsNavigable(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();

        // Browsers strip control characters before resolving a URL, which is how
        // "java\tscript:" gets through a naive scheme check. Nothing legitimate
        // needs them.
        if (trimmed.Any(char.IsControl)) return false;

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return true;

        // A colon after the path has begun is part of the path, not a scheme:
        // "notes/10:30.md" is relative, "//host/x" is protocol-relative.
        var pathStart = trimmed.IndexOfAny(['/', '?', '#']);
        if (pathStart >= 0 && pathStart < colon) return true;

        var scheme = trimmed[..colon];
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }

    private static void Wrap(RenderTreeBuilder builder, ref int seq, string element, string? className, string text)
    {
        builder.OpenElement(seq++, element);
        if (className is not null) builder.AddAttribute(seq++, "class", className);
        builder.AddContent(seq++, text);
        builder.CloseElement();
    }
}
