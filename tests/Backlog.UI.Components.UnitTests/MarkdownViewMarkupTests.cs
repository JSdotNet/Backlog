using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The read view's whole rendering, pinned.
///
/// <para>Written when the block half of <c>MarkdownView</c> moved out into
/// <c>MarkdownBlockView</c> and its three sub-components. The suites around this
/// one assert a document construct at a time — a table's alignment, a footnote's
/// back-link, a checklist item's role — which is the right shape for a rule about
/// what markdown means, and blind to what a move like that risks: a class that
/// quietly went missing three elements away from whatever the test was looking
/// at. This compares the entire rendering against what was captured from the
/// private fragments before they were deleted.</para>
///
/// <para>One deliberate difference from that capture, in two places, both of them
/// a rule. A fully static <c>&lt;hr class="md-divider" /&gt;</c> is folded by the
/// Razor compiler into a literal HTML string and normalised to
/// <c>&lt;hr class="md-divider"&gt;</c>; the class now arrives as a parameter, so
/// it is a real element frame and bUnit serialises that frame as
/// <c>&lt;hr class="md-divider" /&gt;</c>. The two parse to the identical DOM —
/// this is how a render tree is written out as text, not what a browser is handed
/// — and every rule in this repository that looks at a divider does so with a CSS
/// selector.</para>
///
/// <para>The document also holds a mermaid fence, so the id <c>DiagramView</c>
/// mints per instance and the element reference bUnit writes beside it are
/// normalised away: both are new on every render and neither is this view's.</para>
/// </summary>
public sealed class MarkdownViewMarkupTests
{
    [Fact]
    public void A_read_view_renders_what_it_has_always_rendered()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(read => read.Blocks, MarkdownGoldenSample.Blocks));

        Assert.Equal(Normalize(ReadMarkup), Normalize(view.Markup));
    }

    /// <summary>Line endings, so neither side depends on how this file was checked
    /// out, and the two per-render identifiers DiagramView mints.</summary>
    private static string Normalize(string markup) =>
        Regex.Replace(
            Regex.Replace(markup.Replace("\r\n", "\n"), "diagram-[0-9a-f]{32}", "diagram-ID"),
            " blazor:elementReference=\"[0-9a-f-]{36}\"",
            string.Empty);

    // Written flush left, closing delimiter included. A raw string literal strips
    // whatever indentation its closing delimiter carries from every line, and the
    // indentation inside this markup is not decoration: Razor emits the whitespace
    // between two sibling elements as content, so the columns in front of a
    // <tbody> and in front of a footnote's back-link are part of the rendering.
    private const string ReadMarkup = """
<div class="md-view"><p class="md-heading md-heading--1" role="heading" aria-level="1">A heading</p><p class="md-p">A paragraph with <strong>bold</strong>, <em>emphasis</em>, <code class="md-inline-code">inline code</code>, a <span class="tag-chip">#tag</span> and a <a class="md-link" href="https://example.com" target="_blank" rel="noopener noreferrer">link</a>, and a footnote<sup class="md-fnref" id="fnref-note"><a href="#fn-note" aria-label="Footnote 1">1</a></sup>.</p><ul class="md-list"><li class=" ">A bullet<ul class="md-list"><li class=" ">Nested under it</li></ul><ol class="md-list"><li>Numbers under a bullet</li></ol></li><li class="md-item--task md-item--done"><span class="md-check md-check--done" data-testid="entry-checkbox" role="img" aria-label="Done">✔</span>A finished task</li><li class="md-item--task "><span class="md-check " data-testid="entry-checkbox" role="img" aria-label="Not done"></span>An unfinished one</li><li class=" ">A plain bullet in the same list</li></ul><ol class="md-list"><li>First ordered item</li><li>Second ordered item</li></ol><blockquote class="md-quote">A quote, which may run over more than one line.</blockquote><div class="md-table-scroll" role="region" tabindex="0" aria-label="Table"><table class="md-table"><thead><tr><th scope="col" class="md-table__cell--left">Left</th><th scope="col" class="md-table__cell--center">Middle</th><th scope="col" class="md-table__cell--right">Right</th></tr></thead>
                    <tbody><tr><td class="md-table__cell--left">one</td><td class="md-table__cell--center">two</td><td class="md-table__cell--right">three</td></tr><tr><td class="md-table__cell--left">four</td><td class="md-table__cell--center">five</td><td class="md-table__cell--right"></td></tr></tbody></table></div><pre class="md-code"><code>var blocks = MarkdownPreview.ParseDocument(source);</code></pre><figure class="diagram-view" data-testid="diagram-view" aria-label="mermaid diagram source visualization"><figcaption class="diagram-view__header"><span class="diagram-view__title">Backlog diagram</span><span class="language-badge diagram-view__language" data-testid="diagram-view-language">mermaid</span></figcaption>
    <div class="diagram-view__canvas" role="img" aria-label="mermaid diagram source visualization"><div class="diagram-view__rendered" data-diagram-id="diagram-ID"><p class="diagram-view__status" role="status">Rendering mermaid diagram...</p></div></div><details class="diagram-view__details"><summary>Diagram source</summary>
            <pre class="diagram-view__source"><code>graph TD; a--&gt;b;</code></pre></details></figure><hr class="md-divider" /><aside class="md-footnotes" aria-label="Footnotes"><hr class="md-divider" />
                <ol class="md-footnotes__list"><li id="fn-note" class="md-footnotes__item">Notes are collected at the bottom.
                            <a class="md-footnotes__back" href="#fnref-note" aria-label="Back to reference 1">↩</a></li></ol></aside></div>
""";
}
