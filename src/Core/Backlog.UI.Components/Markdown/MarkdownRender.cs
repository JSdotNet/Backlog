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

    /// <summary>
    /// A second reading of what an inline points at, offered to the caller before
    /// the default rendering is chosen. It is handed the target — a code span's
    /// own text, or a link's or an image's URL — and the words the author wrote,
    /// which for an image is its alt text, and it returns the fragment to draw
    /// instead, or <see langword="null"/> to leave that inline exactly as it has
    /// always rendered.
    /// <para>
    /// A hook rather than a rule baked in here, because the same code span means
    /// different things in different places: <c>.domain/sessions/domain.md</c> in
    /// a knowledge chapter is a chapter somebody can open, and the identical span
    /// in an entry's body is a path being quoted. Only the caller knows which
    /// document it is rendering.
    /// </para>
    /// <para>
    /// Which inline it came from travels with it, because the two are not the same
    /// question. A link was written to be followed and the author already said so
    /// with the syntax, so resolving it against the document around it is reading
    /// what they meant; a code span was written to be read, and <c>domain.md</c> in
    /// one is the most quotable file name in the repository. Without the
    /// distinction the caller would have to guess it from the target matching the
    /// text, which a link whose words are its own URL also does.
    /// </para>
    /// <para>
    /// An image is a third question again, and the sharpest of the three: its
    /// target is fetched with no click to consent to it, so a caller that cannot
    /// place one is not choosing between a link and plain words but between a
    /// request it cannot vouch for and none. A relative <c>src</c> is also the one
    /// target with nowhere to resolve <em>to</em> — a knowledge chapter's picture
    /// sits beside it on disk, outside any <c>wwwroot</c>, so there is no origin
    /// the path could be right about.
    /// </para>
    /// </summary>
    public delegate RenderFragment? MarkdownInlineTarget(string target, string text, MarkdownInlineKind kind);

    public static RenderFragment Inlines(IReadOnlyList<MdInline> parts) => Inlines(parts, null);

    public static RenderFragment Inlines(IReadOnlyList<MdInline> parts, MarkdownInlineTarget? target) => builder =>
    {
        var seq = 0;
        foreach (var part in parts)
        {
            switch (part)
            {
                case MdText text:
                    builder.AddContent(seq++, text.Text);
                    break;

                // All three hooked cases come first so the caller gets the refusal —
                // returning null — rather than having to reproduce the default it
                // is declining to replace.
                case MdCodeSpan codeSpan when target?.Invoke(codeSpan.Text, codeSpan.Text, MarkdownInlineKind.CodeSpan) is { } replacement:
                    builder.AddContent(seq++, replacement);
                    break;
                case MdLink hooked when target?.Invoke(hooked.Url, hooked.Text, MarkdownInlineKind.Link) is { } replacement:
                    builder.AddContent(seq++, replacement);
                    break;
                // The alt text is what an image says when the picture is not there,
                // which makes it the right thing to hand a hook that may decide the
                // picture should not be fetched at all.
                case MdImage hooked when target?.Invoke(hooked.Url, hooked.Alt, MarkdownInlineKind.Image) is { } replacement:
                    builder.AddContent(seq++, replacement);
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

        if (!NamesScheme(trimmed)) return true;

        var scheme = trimmed[..trimmed.IndexOf(':', StringComparison.Ordinal)];
        return scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
            || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a link target opens with something a browser would read as a URL
    /// scheme, rather than being relative to the document it was written in.
    ///
    /// <para>Public because a caller deciding what to do with a target it could not
    /// place has to know which of two answers it is looking at. A relative target
    /// that resolves nowhere is the author's mistake and belongs on screen as the
    /// words they wrote; one that names a scheme was never the caller's to place,
    /// and handing it back here is what keeps an <c>https</c> link the anchor it
    /// has always been. Asking the question in one place is what stops the two
    /// readings drifting: <see cref="IsNavigable"/>'s allow-list decides which
    /// schemes are welcome, and this decides only whether there is one.</para>
    /// </summary>
    public static bool NamesScheme(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        var trimmed = url.Trim();

        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0) return false;

        // A colon after the path has begun is part of the path, not a scheme:
        // "notes/10:30.md" is relative, "//host/x" is protocol-relative.
        var pathStart = trimmed.IndexOfAny(['/', '?', '#']);
        return pathStart < 0 || pathStart > colon;
    }

    private static void Wrap(RenderTreeBuilder builder, ref int seq, string element, string? className, string text)
    {
        builder.OpenElement(seq++, element);
        if (className is not null) builder.AddAttribute(seq++, "class", className);
        builder.AddContent(seq++, text);
        builder.CloseElement();
    }
}

/// <summary>
/// Which inline a <see cref="MarkdownRender.MarkdownInlineTarget"/> is being asked
/// about. Three members and no more, because those are all the inlines that carry
/// a target at all: two a reader may follow, and one the browser follows for them.
/// </summary>
public enum MarkdownInlineKind
{
    /// <summary>A code span, whose target is its own text. The convention writes a
    /// reference this way, but so does everything else that wants monospace, so a
    /// caller reading one is looking at a path far less often than at a command or
    /// a single word.</summary>
    CodeSpan,

    /// <summary>A markdown link, whose target is its URL. The author wrote the
    /// syntax of a thing to be followed, which is the difference that lets a
    /// caller resolve it against the document around it.</summary>
    Link,

    /// <summary>An image, whose target is its URL and whose words are its alt
    /// text. Resolved against the document like a link is, because the author
    /// wrote a path meaning the file beside them — but the answer a caller gives
    /// is not a link's answer: a picture it cannot place is a request nobody asked
    /// for, so refusing it leaves the alt text and no <c>src</c> at all.</summary>
    Image
}
