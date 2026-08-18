using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Code;

/// <summary>
/// The inside of the <c>&lt;pre&gt;</c>, built here rather than in markup for
/// one reason: inside a <c>pre</c> every space and newline is visible, and Razor
/// is entitled to add its own around nested markup. Building the tree by hand
/// means the block contains exactly the source and nothing else.
/// <para>
/// Text goes through the render tree's normal escaping, so a snippet cannot
/// inject markup.
/// </para>
/// </summary>
public static class CodeRender
{
    /// <summary>Each line is an element of its own so the gutter can number it
    /// with a CSS counter. Numbering in the markup would put the numbers in the
    /// clipboard when a reader selects the block by hand.</summary>
    public static RenderFragment Lines(IReadOnlyList<CodeLine> lines) => builder =>
    {
        var seq = 0;

        foreach (var line in lines)
        {
            builder.OpenElement(seq++, "span");
            builder.AddAttribute(seq++, "class", "code-view__line");

            foreach (var token in line.Tokens)
            {
                // Plain text is the bulk of any snippet and needs no colour, so
                // it stays bare text instead of a span that styles nothing.
                if (token.Kind == CodeTokenKind.Plain)
                {
                    builder.AddContent(seq++, token.Text);
                    continue;
                }

                builder.OpenElement(seq++, "span");
                builder.AddAttribute(seq++, "class", token.CssClass);
                builder.AddContent(seq++, token.Text);
                builder.CloseElement();
            }

            builder.CloseElement();
        }
    };
}
