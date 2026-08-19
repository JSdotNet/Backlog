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
    /// The roadmap band's 30% is a ceiling, not a share: the band takes only the
    /// height its content needs and never more than three tenths, so the pane row
    /// keeps at least seven tenths whatever the band later grows into. A bare
    /// <c>30%</c> would reserve the share while the band is empty; a bare
    /// <c>max-content</c> would let a timeline push the panes off the screen.
    /// <para>
    /// It was a quarter while the band held a placeholder. A quarter fits an axis and
    /// three 2rem rows at a 900px-tall window, and a plan filed across two
    /// repositories with a lane each is already four rows, so a quarter meant a
    /// scrollbar in a band that had room for the plan everywhere but the last row.
    /// The five percent comes off the pane row deliberately.
    /// </para>
    /// <para>
    /// The spelling is load-bearing, which is why it is asserted literally.
    /// <c>min(30%, max-content)</c> says the intent more plainly and is a parse
    /// error — <c>min()</c> is a math function and takes numeric arguments only, so
    /// a track breadth keyword inside one drops the whole declaration and leaves the
    /// workspace with no explicit rows. <c>fit-content(30%)</c> is defined as that
    /// same <c>min(max-content, 30%)</c> and is valid here. This assertion exists to
    /// stop the invalid form being "restored" as a readability fix.
    /// </para>
    /// </summary>
    [Fact]
    public void The_roadmap_band_is_capped_and_never_reserves_the_cap()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".workspace {", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The workspace needs a rule stacking the band above the pane row.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The workspace rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains("grid-template-rows: fit-content(30%) minmax(0, 1fr);", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("min(30%", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", rule, StringComparison.Ordinal);

        // The cap is only firm because the band's automatic minimum size is 0,
        // which is what `overflow: hidden` on the band buys. Without it,
        // fit-content() would let min-content win and the band could exceed 25%.
        var bandStart = css.IndexOf(".roadmap-band {", StringComparison.Ordinal);
        Assert.True(bandStart >= 0, "The band needs its own rule.");

        var band = css[bandStart..css.IndexOf("}\n", bandStart, StringComparison.Ordinal)];

        Assert.Contains("min-height: 0;", band, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", band, StringComparison.Ordinal);

        // With no band there is no empty track to leave behind.
        Assert.Contains(
            ".workspace--no-roadmap {\n    grid-template-rows: minmax(0, 1fr);\n}",
            css,
            StringComparison.Ordinal);

        // The band caps its own content instead of the pane row absorbing it. What
        // the content row no longer does is scroll: it hands its height down to the
        // chart, and the chart's own frame is what scrolls, so the heading and the
        // filters cannot be scrolled away from the plan they describe.
        var contentStart = css.IndexOf(".roadmap-band__content {", StringComparison.Ordinal);
        Assert.True(contentStart >= 0, "The band's content row needs its own rule.");

        var content = css[contentStart..css.IndexOf("}\n", contentStart, StringComparison.Ordinal)];

        Assert.Contains("min-height: 0;", content, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", content, StringComparison.Ordinal);

        // A 1fr row with a zero automatic minimum is what passes the height down. A
        // plain `1fr` could not go below its content, and a track that cannot do that
        // cannot be scrolled inside.
        Assert.Contains("grid-template-rows: minmax(0, 1fr);", content, StringComparison.Ordinal);
        Assert.Contains("align-content: stretch;", content, StringComparison.Ordinal);

        var chartStart = css.IndexOf(".roadmap-band__timeline {", StringComparison.Ordinal);
        Assert.True(chartStart >= 0, "The chart needs a band-scoped rule telling it to fill rather than grow.");

        var chart = css[chartStart..css.IndexOf("}\n", chartStart, StringComparison.Ordinal)];

        Assert.Contains("min-height: 0;", chart, StringComparison.Ordinal);
        Assert.Contains("grid-template-rows: auto minmax(0, 1fr);", chart, StringComparison.Ordinal);

        // And the scrollbar sits in the diagram, on the frame that holds the rows.
        var frameStart = css.IndexOf(".roadmap-band__timeline .roadmap-timeline__frame {", StringComparison.Ordinal);
        Assert.True(frameStart >= 0, "The chart's frame should be the thing that scrolls.");

        var frame = css[frameStart..css.IndexOf("}\n", frameStart, StringComparison.Ordinal)];

        Assert.Contains("min-height: 0;", frame, StringComparison.Ordinal);
        // `auto`, not `scroll`: a plan that fits shows no scrollbar at all.
        Assert.Contains("overflow-y: auto;", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("overflow-y: scroll;", frame, StringComparison.Ordinal);
    }

    /// <summary>
    /// The height ceiling worked and the content inside it did not. The library's
    /// <c>.empty-state</c> is shaped for a large empty pane — <c>--spacing-xl</c>
    /// padding all round and an <c>--font-size-xl</c> title, about 140px — and a
    /// a capped band on a short window has nothing like that left after its own
    /// padding, border, header and gap. Measured at a 620px viewport height the
    /// band's content box was 28px against a 140px scroll height, and at 450px it
    /// was 0px: the placeholder's title and description were not truncated, they
    /// were 0% visible, while the heading above them stayed and read as a band
    /// over nothing.
    /// <para>
    /// The compacting is asserted against the application stylesheet rather than
    /// the library's because that is where it has to live. Editing
    /// <c>.empty-state</c> would reshape every other empty state in the product,
    /// all of which sit in panes that have the room.
    /// </para>
    /// <para>
    /// Compacting it is also what makes <c>fit-content()</c> behave as the ceiling it
    /// is named for. While the band's <c>max-content</c> height exceeded the cap at
    /// every tested viewport the ratio pinned at exactly the cap each time, so the
    /// track was a fixed height wearing a cap's clothing.
    /// </para>
    /// </summary>
    [Fact]
    public void The_bands_placeholder_is_compacted_so_it_fits_inside_the_cap()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var emptyStart = css.IndexOf(".roadmap-band__empty {", StringComparison.Ordinal);
        Assert.True(emptyStart >= 0,
            "The band needs its own placeholder rule; the library's empty state does not fit a capped band on a short window.");

        var empty = css[emptyStart..css.IndexOf("}\n", emptyStart, StringComparison.Ordinal)];

        // A pane's --spacing-xl is what made the placeholder 140px tall.
        Assert.Contains("padding: var(--spacing-sm) var(--spacing-md);", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("--spacing-xl", empty, StringComparison.Ordinal);

        // A band is read across, and the timeline replacing this starts at its left edge.
        Assert.Contains("text-align: left;", empty, StringComparison.Ordinal);

        var titleStart = css.IndexOf(".roadmap-band__empty .empty-state__title {", StringComparison.Ordinal);
        Assert.True(titleStart >= 0, "The placeholder's title needs a size proportionate to a band.");

        var title = css[titleStart..css.IndexOf("}\n", titleStart, StringComparison.Ordinal)];

        Assert.Contains("font-size: var(--font-size-base);", title, StringComparison.Ordinal);
        Assert.DoesNotContain("var(--font-size-xl)", title, StringComparison.Ordinal);

        // The band's own padding is a band's, not a pane's.
        var bandStart = css.IndexOf(".roadmap-band {", StringComparison.Ordinal);
        var band = css[bandStart..css.IndexOf("}\n", bandStart, StringComparison.Ordinal)];

        Assert.Contains("padding: var(--spacing-sm) var(--spacing-lg);", band, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: var(--spacing-lg);", band, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two steps down a short window takes, and the arithmetic behind them. The
    /// workspace is the viewport less 105px of shell padding, shell gap and app
    /// header, so the band's capped track is (viewport - 105) / 4.
    /// <list type="bullet">
    /// <item>Compacted, the band measures 145.5px, first covered by a quarter at
    /// (145.5 x 4) + 105 = 687px. Below roughly 43rem the description clips again,
    /// so it goes at 48rem — one honest line of placeholder rather than a clipped
    /// paragraph, with headroom for a description that wraps at a narrow width.</item>
    /// <item>Without the description the band measures 98.5px, first covered at
    /// (98.5 x 4) + 105 = 499px. The band goes entirely at 34rem, above that onset:
    /// a quarter of a window that short cannot hold even a heading plus a line, and
    /// a heading over blank space reads as broken.</item>
    /// </list>
    /// Both values are steps this stylesheet's breakpoint ladder already uses on the
    /// inline axis, so the short-window steps are not a second, private scale.
    /// </summary>
    [Fact]
    public void A_short_window_drops_the_bands_description_and_then_the_band()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var descriptionStep = Block(css, "@media (max-height: 48rem) {");

        Assert.Contains(".roadmap-band__empty .empty-state__body {\n        display: none;\n    }",
            descriptionStep, StringComparison.Ordinal);
        Assert.Contains("padding-block: var(--spacing-sm);", descriptionStep, StringComparison.Ordinal);
        Assert.Contains("padding-block: var(--spacing-xs);", descriptionStep, StringComparison.Ordinal);

        // The eyebrow, the heading and the placeholder's title are the point of the
        // step: they are what stays.
        Assert.DoesNotContain(".roadmap-band__eyebrow", descriptionStep, StringComparison.Ordinal);
        Assert.DoesNotContain(".roadmap-band__title", descriptionStep, StringComparison.Ordinal);
        Assert.DoesNotContain(".roadmap-band {\n        display: none;", descriptionStep, StringComparison.Ordinal);

        // This step reshapes what is inside the band and nothing about the grid, so
        // the two steps cannot fight over the workspace rows.
        Assert.DoesNotContain(".workspace", descriptionStep, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(descriptionStep, "display: none;"));

        var hiddenStep = Block(css, "@media (max-height: 34rem) {");

        // Unscoped, both of them: the band has one on-screen size, so there is no
        // smaller state for a short window to fall back to and nothing to carve out
        // of these rules. Hiding it here does not take away the way back either — the
        // control that shows it again is in the app header, not on the band.
        Assert.Contains("\n    .roadmap-band {\n        display: none;\n    }", hiddenStep, StringComparison.Ordinal);

        // Hiding the band is only half of it. Leaving the track behind would hand
        // the pane row an empty quarter and the gap above it.
        Assert.Contains("\n    .workspace {\n        grid-template-rows: minmax(0, 1fr);\n        row-gap: 0;\n    }",
            hiddenStep, StringComparison.Ordinal);

        // And no scoped survivor beside them: a `:not()` here would be a leftover
        // from a collapsed state the band no longer has.
        Assert.DoesNotContain("workspace--roadmap-collapsed", css, StringComparison.Ordinal);

        // The steps have to come after the inline-axis query that also sets the
        // band's padding, or that one wins where a viewport is both narrow and short.
        var narrowStep = css.IndexOf("@media (max-width: 72rem) {", StringComparison.Ordinal);
        Assert.True(narrowStep >= 0, "The narrow-viewport band rule should still exist.");
        Assert.True(css.IndexOf("@media (max-height: 48rem) {", StringComparison.Ordinal) > narrowStep,
            "The short-window steps must come later in the cascade than the narrow-width one.");
        Assert.True(css.IndexOf("@media (max-height: 34rem) {", StringComparison.Ordinal)
            > css.IndexOf("@media (max-height: 48rem) {", StringComparison.Ordinal),
            "The band-hidden step must come after the description step it overrides.");
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

        foreach (var selector in new[] { ".side-pane-stack {", ".knowledge-layout--inbox-before-backlog > .inbox-pane {" })
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

    /// <summary>
    /// The band gives the space back rather than reserving it, and both of these
    /// assertions are about the same thing: nothing anywhere pins the band's height,
    /// so what it measures is what it draws and the pane row takes the rest.
    /// <list type="bullet">
    /// <item>The base rows stay a cap, not a share. <c>fit-content(30%)</c> is the one
    /// spelling that means <c>min(max-content, 30%)</c> here; a second
    /// <c>grid-template-rows</c> in the same block would decide it instead.</item>
    /// <item>The one modifier the grid has drops the track outright. A <c>height</c>
    /// of any kind on it would reserve that much for a band that is not being
    /// rendered, which is the reserved share the cap was written to avoid, only
    /// emptier.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Nothing_pins_the_bands_height_at_any_step()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));

        var workspaceStart = css.IndexOf(".workspace {", StringComparison.Ordinal);
        Assert.True(workspaceStart >= 0, "The workspace grid should still exist.");

        var workspace = css[workspaceStart..css.IndexOf("}\n", workspaceStart, StringComparison.Ordinal)];

        Assert.Contains("grid-template-rows: fit-content(30%) minmax(0, 1fr);", workspace, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(workspace, "grid-template-rows:"));

        var noBandStart = css.IndexOf(".workspace--no-roadmap {", StringComparison.Ordinal);
        Assert.True(noBandStart >= 0, "A workspace with no band needs a row set with no track for one.");

        var noBand = css[noBandStart..css.IndexOf("}\n", noBandStart, StringComparison.Ordinal)];

        Assert.Contains("grid-template-rows: minmax(0, 1fr);", noBand, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(noBand, "grid-template-rows:"));

        foreach (var pin in new[] { "height:", "min-height:", "max-height:" })
        {
            Assert.DoesNotContain(pin, noBand, StringComparison.Ordinal);
        }
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
