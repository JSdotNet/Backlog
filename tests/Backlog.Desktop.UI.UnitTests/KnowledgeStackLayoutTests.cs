namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeStackLayoutTests
{
    [Fact]
    public void Knowledge_stack_keeps_the_side_pane_scroll_layout_last_in_the_cascade()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var sidePaneLayout = css.IndexOf(".knowledge-stack {\n    display: flex;", StringComparison.Ordinal);
        var legacyStickyLayout = css.IndexOf(".knowledge-stack {\n    position: sticky;", StringComparison.Ordinal);

        Assert.True(sidePaneLayout >= 0, "The Knowledge side pane must keep its flex column layout.");
        Assert.True(legacyStickyLayout < 0 || legacyStickyLayout < sidePaneLayout,
            "A later sticky/grid Knowledge stack block overrides the side-pane flex layout and stretches the section tabs vertically.");
    }

    [Fact]
    public void Knowledge_section_tabs_do_not_stretch_to_the_pane_height()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var navRuleStart = css.IndexOf(".knowledge-stack__repositories,", StringComparison.Ordinal);

        Assert.True(navRuleStart >= 0, "The Knowledge section navigation rule should exist.");

        var nextRuleStart = css.IndexOf(".knowledge-stack__section", navRuleStart, StringComparison.Ordinal);
        Assert.True(nextRuleStart > navRuleStart, "The Knowledge section rule should follow the nav rule.");

        var navRule = css[navRuleStart..nextRuleStart];

        Assert.Contains("align-items: flex-start;", navRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_and_architecture_outer_panels_clip_instead_of_owning_the_scrollbar()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".knowledge-stack__section > .knowledge-pane--arc42,", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "Architecture and Domain panels should share an outer containment rule.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The outer containment rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains(".knowledge-stack__section > .domain-knowledge", rule, StringComparison.Ordinal);
        Assert.Contains("max-height: 100%;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow: auto;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_and_architecture_documents_own_the_knowledge_scrollbar()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".knowledge-stack__section > .knowledge-pane--arc42 .knowledge-document,", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "Architecture and Domain documents should share the scroll container rule.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The document scroll rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains(".knowledge-stack__section > .domain-knowledge > .domain-document", rule, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto;", rule, StringComparison.Ordinal);
        Assert.Contains("max-height: 100%;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: auto;", rule, StringComparison.Ordinal);
        Assert.Contains("scrollbar-gutter: stable;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chapter_shown_through_the_file_view_is_framed_once()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var cardRuleStart = css.IndexOf(".design-token,\n.design-section,\n.domain-document,\n", StringComparison.Ordinal);
        Assert.True(cardRuleStart >= 0, "The knowledge card rule should still exist for the documents that are lists of files rather than one file.");

        var cardRuleEnd = css.IndexOf("}\n", cardRuleStart, StringComparison.Ordinal);
        Assert.True(cardRuleEnd > cardRuleStart, "The knowledge card rule should be complete.");

        var cardRule = css[cardRuleStart..cardRuleEnd];

        // The architecture article holds a file view on every render, and the file
        // view draws its own edge, so a card here is a card around a card.
        Assert.DoesNotContain(".knowledge-document", cardRule, StringComparison.Ordinal);

        var withdrawalStart = css.IndexOf(".domain-document--chapter {", StringComparison.Ordinal);
        Assert.True(withdrawalStart > cardRuleStart,
            "The domain chapter must withdraw the card after the rule that sets it, or the card wins on source order.");

        var withdrawal = css[withdrawalStart..css.IndexOf("}\n", withdrawalStart, StringComparison.Ordinal)];

        // The inset goes with the edge. A frame's padding without its border is a
        // gap with nothing to explain it.
        Assert.Contains("padding: 0;", withdrawal, StringComparison.Ordinal);
        Assert.Contains("border: 0;", withdrawal, StringComparison.Ordinal);
        Assert.Contains("background: transparent;", withdrawal, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_metadata_keeps_consecutive_relations_apart()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        // Both rules were deleted once already, by a commit that was replacing the
        // block beneath them, and the strip spent that time rendering its chips as
        // one unbroken run of text — two relation paths reading as one. The gap is
        // the whole of what was lost, so the gap is what is pinned.
        var stripStart = css.IndexOf(".domain-metadata {", StringComparison.Ordinal);
        Assert.True(stripStart >= 0, "The domain metadata strip needs its own rule; without one its chips run together.");

        var strip = css[stripStart..css.IndexOf("}\n", stripStart, StringComparison.Ordinal)];

        Assert.Contains("display: flex;", strip, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", strip, StringComparison.Ordinal);

        var itemStart = css.IndexOf(".domain-metadata__item {", StringComparison.Ordinal);
        Assert.True(itemStart >= 0, "A metadata chip needs its own rule; a chip pinning two relations has to keep them apart inside it.");

        var item = css[itemStart..css.IndexOf("}\n", itemStart, StringComparison.Ordinal)];

        Assert.Contains("display: inline-flex;", item, StringComparison.Ordinal);
        Assert.Contains("gap: var(--spacing-xs);", item, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_body_constrains_tall_panels_to_the_available_pane_height()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".knowledge-stack__body {", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Knowledge body layout rule should exist.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The Knowledge body layout rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("display: grid;", rule, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", rule, StringComparison.Ordinal);
        Assert.Contains("align-items: stretch;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("align-items: start;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Knowledge_menu_content_aligns_to_the_top()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".knowledge-stack__menu {", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Knowledge menu rule should exist.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The Knowledge menu rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("display: grid;", rule, StringComparison.Ordinal);
        Assert.Contains("align-content: start;", rule, StringComparison.Ordinal);
        Assert.Contains("grid-auto-rows: max-content;", rule, StringComparison.Ordinal);
    }
    [Fact]
    public void Tools_pane_stays_right_docked_when_it_is_the_only_side_pane()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".side-pane-stack--full.side-pane-stack--right-docked", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Tools pane must not stretch into a horizontal full-width pane when Backlog is hidden.");

        var nextRuleStart = css.IndexOf("\n.", ruleStart + 1, StringComparison.Ordinal);
        var rule = css[ruleStart..(nextRuleStart < 0 ? css.Length : nextRuleStart)];

        Assert.Contains("width: min(36rem, 100%);", rule, StringComparison.Ordinal);
        Assert.Contains("margin-left: auto;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_repository_scope_chip_keeps_readable_brand_state()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var scopeRuleStart = css.IndexOf(".chip--scope {", StringComparison.Ordinal);
        var activeScopeRuleStart = css.IndexOf(".chip--scope.chip--active {", StringComparison.Ordinal);

        Assert.True(scopeRuleStart >= 0, "Repository scope chips should have their own base surface rule.");
        Assert.True(activeScopeRuleStart > scopeRuleStart,
            "The selected repository scope chip must override the base scope background so inverse text is not rendered on a dark surface.");

        var activeScopeRuleEnd = css.IndexOf("}\n", activeScopeRuleStart, StringComparison.Ordinal);
        Assert.True(activeScopeRuleEnd > activeScopeRuleStart, "The selected repository scope chip rule should be complete.");

        var activeScopeRule = css[activeScopeRuleStart..activeScopeRuleEnd];

        Assert.Contains("background: var(--color-primary);", activeScopeRule, StringComparison.Ordinal);
        Assert.Contains("color: var(--color-text-inverse);", activeScopeRule, StringComparison.Ordinal);
        Assert.Contains("border-color: var(--color-primary);", activeScopeRule, StringComparison.Ordinal);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindAppCss() =>
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css");
}
