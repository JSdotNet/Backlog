using System.Text.RegularExpressions;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The markup the two knowledge block renderers emit, pinned whole.
///
/// <para>Written when their block switches moved into the shared library. Every
/// other test of these panels finds an element and asks about it, which is the
/// right shape for a rule but blind to what a move like that actually risks: a
/// class that quietly went missing three elements away from whatever the test was
/// looking at. So this compares the entire rendering, once per renderer, against
/// the markup captured from the hand-rolled switches before they were
/// deleted.</para>
///
/// <para>One deliberate difference from that capture, in one place. A rule used to
/// be written as fully static markup, which the Razor compiler folds into a
/// literal HTML string and normalises to <c>&lt;hr class="knowledge-divider"&gt;</c>;
/// its class now arrives as a parameter, so it is a real element frame and bUnit
/// serialises that frame as <c>&lt;hr class="knowledge-divider" /&gt;</c>. The two
/// parse to the identical DOM — this is how a render tree is written out as text,
/// not what a browser is handed — and every rule in this repository that looks at
/// a divider does so with a CSS selector.</para>
///
/// <para>Two things are normalised away because they are new on every render and
/// belong to <c>DiagramView</c> rather than to these panels: the id it mints per
/// instance, and the element reference bUnit writes beside it.</para>
///
/// <para>Both captures have been moved once since, deliberately: the language
/// badge is now inside a <c>diagram-view__badges</c> span, because a diagram drawn
/// from a generated Archify artifact carries a second badge naming the renderer
/// and the two have to sit together. The badge itself is untouched — same class,
/// same <c>data-testid</c>, same text — and the wrapper is unconditional, so a
/// chapter with no artifact renders it holding only the language, which is what
/// these four captures show. Recorded because this suite exists to make markup
/// movement visible, and a test named for what has always rendered should say when
/// the answer changed and why.</para>
/// </summary>
public sealed class KnowledgeBlockMarkupTests
{
    [Fact]
    public void The_design_sections_blocks_render_what_they_have_always_rendered()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<KnowledgeBlocks>(parameters => parameters
            .Add(blocks => blocks.Blocks, KnowledgeGoldenSamples.DesignBlocks));

        Assert.Equal(Normalize(DesignMarkup), Normalize(view.Markup));
    }

    [Fact]
    public void An_arc42_documents_blocks_render_what_they_have_always_rendered()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<KnowledgeMarkdownView>(parameters => parameters
            .Add(blocks => blocks.Blocks, KnowledgeGoldenSamples.Arc42Blocks));

        Assert.Equal(Normalize(Arc42Markup), Normalize(view.Markup));
    }

    /// <summary>Line endings, so the expectation does not depend on how this file
    /// was checked out, and the two per-render identifiers DiagramView mints.</summary>
    private static string Normalize(string markup) =>
        Regex.Replace(
            Regex.Replace(markup.Replace("\r\n", "\n"), "diagram-[0-9a-f]{32}", "diagram-ID"),
            " blazor:elementReference=\"[0-9a-f-]{36}\"",
            string.Empty);

    // The expectations are written flush left, closing delimiter included. A raw
    // string literal strips whatever indentation its closing delimiter carries
    // from every line, and the indentation inside this markup is not decoration:
    // Razor emits the whitespace between two sibling elements as content, so the
    // twenty columns in front of a <tbody> are part of the rendering.
    private const string DesignMarkup = """
<h5 class="knowledge-subheading">Tokens</h5><p class="knowledge-p">A paragraph with <strong>bold</strong>, <code class="md-inline-code">code</code>, a <span class="tag-chip">#tag</span> and a <a class="md-link" href="https://example.com" target="_blank" rel="noopener noreferrer">link</a>.</p><ul class="knowledge-list"><li>A bullet</li><li>Another, with a <span class="tag-chip">#tag</span></li></ul><ol class="knowledge-list"><li>First</li><li>Second</li></ol><blockquote class="knowledge-quote">A quote that says something.</blockquote><div class="knowledge-table-wrap "><table class="knowledge-table"><thead><tr><th>Name</th><th>Meaning</th></tr></thead>
                    <tbody><tr><td>one</td><td>the first</td></tr><tr><td>two</td><td>the second</td></tr></tbody></table></div><div class="knowledge-table-wrap knowledge-table-wrap--tokens"><table class="knowledge-table"><thead><tr><th>Token</th><th>Value</th></tr></thead>
                    <tbody><tr><td>--surface</td><td>#101014</td></tr></tbody></table></div><pre class="knowledge-code"><code>var blocks = DesignKnowledge.Parse(source);</code></pre><figure class="diagram-view" data-testid="diagram-view" aria-label="plantuml diagram source visualization"><figcaption class="diagram-view__header"><span class="diagram-view__title">Diagram</span>
        <span class="diagram-view__badges"><span class="language-badge diagram-view__language" data-testid="diagram-view-language">plantuml</span></span></figcaption>
    <div class="diagram-view__canvas" role="img" aria-label="plantuml diagram source visualization"><pre class="diagram-view__source"><code>@startuml
A -&gt; B
@enduml</code></pre></div></figure><figure class="diagram-view" data-testid="diagram-view" aria-label="mermaid diagram source visualization"><figcaption class="diagram-view__header"><span class="diagram-view__title">Diagram</span>
        <span class="diagram-view__badges"><span class="language-badge diagram-view__language" data-testid="diagram-view-language">mermaid</span></span></figcaption>
    <div class="diagram-view__canvas" role="img" aria-label="mermaid diagram source visualization"><div class="diagram-view__rendered" data-diagram-id="diagram-ID"><p class="diagram-view__status" role="status">Rendering mermaid diagram...</p></div></div></figure><hr class="knowledge-divider" />
""";

    private const string Arc42Markup = """
<section class="knowledge-heading knowledge-heading--1"><h2 class="knowledge-title knowledge-title--1">Introduction and Goals</h2><p class="knowledge-meta-line"><span class="knowledge-status knowledge-status--ready">ready</span><code class="knowledge-ref knowledge-ref--inert">.arc42/02-constraints.md#technical-constraints</code></p></section><section class="knowledge-heading knowledge-heading--2"><h3 class="knowledge-title knowledge-title--2">Quality Goals</h3></section><section class="knowledge-heading knowledge-heading--3"><h4 class="knowledge-title knowledge-title--3">Stakeholders</h4><p class="knowledge-meta-line"><span class="knowledge-status knowledge-status--draft">draft</span><code class="knowledge-ref knowledge-ref--inert">.domain/context-map.md</code></p></section><p class="knowledge-p">A paragraph with <strong>bold</strong>, <code class="md-inline-code">code</code>, a <span class="tag-chip">#tag</span> and a <a class="md-link" href="https://example.com" target="_blank" rel="noopener noreferrer">link</a>.</p><ul class="knowledge-list"><li>A bullet</li><li>Another, with a <span class="tag-chip">#tag</span></li></ul><ol class="knowledge-list"><li>First</li><li>Second</li></ol><blockquote class="knowledge-quote">A quote that says something.</blockquote><pre class="knowledge-code"><code>var document = KnowledgeMarkdownParser.Parse(path, source);</code></pre><figure class="diagram-view" data-testid="diagram-view" aria-label="mermaid diagram source visualization"><figcaption class="diagram-view__header"><span class="diagram-view__title">Diagram</span>
        <span class="diagram-view__badges"><span class="language-badge diagram-view__language" data-testid="diagram-view-language">mermaid</span></span></figcaption>
    <div class="diagram-view__canvas" role="img" aria-label="mermaid diagram source visualization"><div class="diagram-view__rendered" data-diagram-id="diagram-ID"><p class="diagram-view__status" role="status">Rendering mermaid diagram...</p></div></div></figure><figure class="diagram-view" data-testid="diagram-view" aria-label="c4context diagram source visualization"><figcaption class="diagram-view__header"><span class="diagram-view__title">Code diagram</span>
        <span class="diagram-view__badges"><span class="language-badge diagram-view__language" data-testid="diagram-view-language">c4context</span></span></figcaption>
    <div class="diagram-view__canvas" role="img" aria-label="c4context diagram source visualization"><pre class="diagram-view__source"><code>C4Context
title System</code></pre></div></figure><div class="knowledge-table" role="table"><div class="knowledge-table__row knowledge-table__row--head" role="row"><span role="columnheader">Name | Meaning</span></div><div class="knowledge-table__row " role="row"><span role="cell">one | the first</span></div></div><hr class="knowledge-divider" />
""";
}
