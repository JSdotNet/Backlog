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
/// typed into an entry can inject markup.
/// </para>
/// </summary>
public static class MarkdownRender
{
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
                case MdCodeSpan codeSpan:
                    Wrap(builder, ref seq, "code", "md-inline-code", codeSpan.Text);
                    break;
                case MdTag tag:
                    Wrap(builder, ref seq, "span", "tag-chip", $"#{tag.Tag}");
                    break;
                case MdLink link:
                    builder.OpenElement(seq++, "a");
                    builder.AddAttribute(seq++, "class", "md-link");
                    builder.AddAttribute(seq++, "href", link.Url);
                    builder.AddAttribute(seq++, "target", "_blank");
                    builder.AddAttribute(seq++, "rel", "noopener noreferrer");
                    builder.AddContent(seq++, link.Text);
                    builder.CloseElement();
                    break;
            }
        }
    };

    private static void Wrap(RenderTreeBuilder builder, ref int seq, string element, string? className, string text)
    {
        builder.OpenElement(seq++, element);
        if (className is not null) builder.AddAttribute(seq++, "class", className);
        builder.AddContent(seq++, text);
        builder.CloseElement();
    }
}
