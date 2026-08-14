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
    public void Domain_and_architecture_panels_scroll_within_the_knowledge_pane()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".knowledge-stack__section > .knowledge-pane--arc42,", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "Architecture and Domain panels should share a constrained scroll rule.");

        var ruleEnd = css.IndexOf("}\n", ruleStart, StringComparison.Ordinal);
        Assert.True(ruleEnd > ruleStart, "The constrained scroll rule should be complete.");

        var rule = css[ruleStart..ruleEnd];

        Assert.Contains(".knowledge-stack__section > .domain-knowledge", rule, StringComparison.Ordinal);
        Assert.Contains("max-height: 100%;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow: auto;", rule, StringComparison.Ordinal);
        Assert.Contains("scrollbar-gutter: stable;", rule, StringComparison.Ordinal);
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

    private static string FindAppCss()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "App",
                "Backlog.Desktop.UI",
                "wwwroot",
                "app.css");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate src\\App\\Backlog.Desktop.UI\\wwwroot\\app.css from the test output directory.");
    }
}
