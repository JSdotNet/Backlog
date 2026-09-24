namespace Backlog.Desktop.UI.UnitTests;

public sealed class DevbookStackLayoutTests
{
    [Fact]
    public void Devbook_stack_keeps_the_side_pane_scroll_layout_last_in_the_cascade()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var sidePaneLayout = css.IndexOf(".devbook-stack {\n    display: flex;", StringComparison.Ordinal);
        var legacyStickyLayout = css.IndexOf(".devbook-stack {\n    position: sticky;", StringComparison.Ordinal);

        Assert.True(sidePaneLayout >= 0, "The Devbook side pane must keep its flex column layout.");
        Assert.True(legacyStickyLayout < 0 || legacyStickyLayout < sidePaneLayout,
            "A later sticky/grid Devbook stack block overrides the side-pane flex layout and stretches the section tabs vertically.");
    }

    [Fact]
    public void Devbook_section_tabs_do_not_stretch_to_the_pane_height()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var navRuleStart = css.IndexOf(".devbook-stack__repositories,", StringComparison.Ordinal);

        Assert.True(navRuleStart >= 0, "The Devbook section navigation rule should exist.");

        var nextRuleStart = css.IndexOf(".devbook-stack__section", navRuleStart, StringComparison.Ordinal);
        Assert.True(nextRuleStart > navRuleStart, "The Devbook section rule should follow the nav rule.");

        var navRule = css[navRuleStart..nextRuleStart];

        Assert.Contains("align-items: flex-start;", navRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_and_architecture_outer_panels_clip_instead_of_owning_the_scrollbar()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".devbook-stack__section > .devbook-pane--arc42,", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "Architecture and Domain panels should share an outer containment rule.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The outer containment rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains(".devbook-stack__section > .domain-devbook", rule, StringComparison.Ordinal);
        Assert.Contains("max-height: 100%;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow: auto;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Domain_and_architecture_documents_own_the_knowledge_scrollbar()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".devbook-stack__section > .devbook-pane--arc42 .devbook-document,", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "Architecture and Domain documents should share the scroll container rule.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The document scroll rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains(".devbook-stack__section > .domain-devbook > .domain-document", rule, StringComparison.Ordinal);
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

        var cardRuleStart = css.IndexOf(".design-token,\n.folder-section,\n.domain-document,\n", StringComparison.Ordinal);
        Assert.True(cardRuleStart >= 0, "The knowledge card rule should still exist for the documents that are lists of files rather than one file.");

        var cardRuleEnd = css.IndexOf("}\n", cardRuleStart, StringComparison.Ordinal);
        Assert.True(cardRuleEnd > cardRuleStart, "The knowledge card rule should be complete.");

        var cardRule = css[cardRuleStart..cardRuleEnd];

        // The architecture article holds a file view on every render, and the file
        // view draws its own edge, so a card here is a card around a card.
        Assert.DoesNotContain(".devbook-document", cardRule, StringComparison.Ordinal);

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
    public void Devbook_body_constrains_tall_panels_to_the_available_pane_height()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".devbook-stack__body {", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Devbook body layout rule should exist.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The Devbook body layout rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("display: grid;", rule, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 auto;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", rule, StringComparison.Ordinal);
        Assert.Contains("align-items: stretch;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("align-items: start;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Devbook_menu_content_aligns_to_the_top()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".devbook-stack__menu {", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Devbook menu rule should exist.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The Devbook menu rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("display: grid;", rule, StringComparison.Ordinal);
        Assert.Contains("align-content: start;", rule, StringComparison.Ordinal);
        Assert.Contains("grid-auto-rows: max-content;", rule, StringComparison.Ordinal);
    }
    /// <summary>
    /// The Tools pane used to be docked to the right of the side stack, capped at
    /// 36rem so it did not stretch into a full-width horizontal pane when Backlog
    /// was hidden. It is a full-screen surface now, so that cap is not narrowed —
    /// it is meaningless, and the rule that carried it is gone. What replaces it is
    /// the containment the surface needs instead: the panel inside owns the
    /// scrollbar, so the header holding the control that closes the surface cannot
    /// be scrolled out of reach.
    /// </summary>
    [Fact]
    public void A_full_screen_surface_gives_its_panel_the_scrollbar()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.DoesNotContain(".side-pane-stack--right-docked", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".side-pane-stack > .tools-panel", css, StringComparison.Ordinal);

        var surfaceStart = css.IndexOf(".workspace-surface {", StringComparison.Ordinal);
        Assert.True(surfaceStart >= 0, "A takeover needs a rule that fills the area below the header.");

        var surfaceEnd = css.IndexOf("}\n", surfaceStart, StringComparison.Ordinal);
        Assert.True(surfaceEnd > surfaceStart, "The takeover rule should be complete.");

        var surface = css[surfaceStart..surfaceEnd];

        Assert.Contains("flex: 1 1 auto;", surface, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", surface, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", surface, StringComparison.Ordinal);

        var panelStart = css.IndexOf(".workspace-surface > .tools-panel,", StringComparison.Ordinal);
        Assert.True(panelStart >= 0, "Both surface panels should share the scroll container rule.");

        var panelEnd = css.IndexOf("}\n", panelStart, StringComparison.Ordinal);
        Assert.True(panelEnd > panelStart, "The surface panel rule should be complete.");

        var panel = css[panelStart..panelEnd];

        Assert.Contains(".workspace-surface > .dashboard-panel", panel, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", panel, StringComparison.Ordinal);
        Assert.Contains("overflow: auto;", panel, StringComparison.Ordinal);
    }

    /// <summary>
    /// The workspace is the pane row and nothing else. The roadmap used to sit above
    /// the panes in a track capped at three tenths of the screen; it is a takeover of
    /// its own now, so the workspace grid has one row and no variant for a band.
    /// </summary>
    [Fact]
    public void The_workspace_is_the_pane_row_alone()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var workspace = RuleBody(css, ".workspace {");

        Assert.Contains("grid-template-rows: minmax(0, 1fr);", workspace, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workspace, "grid-template-rows:"));
        Assert.Contains("min-height: 0;", workspace, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", workspace, StringComparison.Ordinal);

        Assert.DoesNotContain("fit-content(30%)", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".workspace--no-roadmap", css, StringComparison.Ordinal);
    }

    /// <summary>
    /// The roadmap fills its surface, and the scrollbar is the chart's: the view and
    /// the cell the chart is drawn in both clip, and hand their height down through
    /// zero-minimum tracks to the chart's frame, which is what scrolls. So the heading
    /// and the filters stay on screen whatever the plan's length.
    /// </summary>
    [Fact]
    public void The_roadmap_fills_its_surface_and_scrolls_inside_the_chart()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains("flex: 1 1 auto;", RuleBody(css, ".workspace-surface > .roadmap-band {"), StringComparison.Ordinal);

        var band = RuleBody(css, "\n.roadmap-band {");
        Assert.Contains("min-height: 0;", band, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", band, StringComparison.Ordinal);

        var body = RuleBody(css, ".roadmap-band__body {");
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", body, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", body, StringComparison.Ordinal);

        var content = RuleBody(css, ".roadmap-band__content {");
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", content, StringComparison.Ordinal);
        Assert.Contains("align-content: stretch;", content, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", content, StringComparison.Ordinal);

        var chart = RuleBody(css, ".roadmap-band__timeline {");
        Assert.Contains("grid-template-rows: auto minmax(0, 1fr);", chart, StringComparison.Ordinal);

        var frame = RuleBody(css, ".roadmap-band__timeline .roadmap-timeline__frame {");
        Assert.Contains("min-height: 0;", frame, StringComparison.Ordinal);
        // `auto`, not `scroll`: a plan that fits shows no scrollbar at all.
        Assert.Contains("overflow-y: auto;", frame, StringComparison.Ordinal);
    }

    /// <summary>
    /// The unplanned work is a column beside the chart while there is any, scrolling on
    /// its own so neither can push the other off the surface — and under the chart,
    /// capped, on a window too narrow for two columns.
    /// </summary>
    [Fact]
    public void The_unplanned_work_sits_beside_the_chart_and_scrolls_on_its_own()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        Assert.Contains(
            "grid-template-columns: minmax(0, 1fr) 22rem;",
            RuleBody(css, ".roadmap-band__body--with-shelf {"),
            StringComparison.Ordinal);

        var shelf = RuleBody(css, ".roadmap-band__shelf {");
        Assert.Contains("min-height: 0;", shelf, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", shelf, StringComparison.Ordinal);

        var narrow = Block(css, "@media (max-width: 72rem) {");
        Assert.Contains(".roadmap-band__body--with-shelf {\n        grid-template-columns: minmax(0, 1fr);", narrow, StringComparison.Ordinal);
        Assert.Contains("max-height: 40vh;", narrow, StringComparison.Ordinal);

        // A surface of its own has the whole height, so the short-window steps that
        // dropped the band's description and then the band are gone.
        Assert.DoesNotContain("@media (max-height: 34rem)", css, StringComparison.Ordinal);
    }

    private static string RuleBody(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        return css[start..css.IndexOf("}\n", start, StringComparison.Ordinal)];
    }

    /// <summary>
    /// Both caps predate anything sitting above the pane row: measured off the
    /// viewport, they overrun their own grid row by however tall the roadmap band
    /// is. The row's own height is the honest ceiling.
    /// </summary>
    [Fact]
    public void Side_panes_are_capped_by_their_row_rather_than_the_viewport()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        foreach (var selector in new[] { ".side-pane-stack {", ".devbook-layout--inbox-before-backlog > .inbox-pane {" })
        {
            var ruleStart = css.IndexOf(selector, StringComparison.Ordinal);
            Assert.True(ruleStart >= 0, $"{selector} should still exist.");

            var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
            Assert.True(ruleEnd > ruleStart, $"{selector} should be complete.");

            var rule = css[ruleStart..ruleEnd];

            Assert.Contains("max-height: 100%;", rule, StringComparison.Ordinal);
            Assert.DoesNotContain("100vh", rule, StringComparison.Ordinal);
        }
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

    /// <summary>The body of the at-rule opened by <paramref name="opening"/>, read
    /// by counting braces rather than by stopping at the first <c>}</c> — inside an
    /// at-rule the first closing brace belongs to a rule within it, not to the
    /// query, so the sibling helpers' <c>IndexOf("}\n")</c> would cut it short.</summary>
    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        var depth = 0;

        for (var index = start + opening.Length - 1; index < css.Length; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return css[start..index];
            }
        }

        Assert.Fail($"{opening} is never closed.");
        return string.Empty;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindAppCss() =>
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css");
}
