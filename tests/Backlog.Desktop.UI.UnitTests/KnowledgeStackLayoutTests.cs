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
    public void Tools_pane_stays_right_docked_when_it_is_the_only_side_pane()
    {
        var css = NormalizeLineEndings(File.ReadAllText(FindAppCss()));
        var ruleStart = css.IndexOf(".side-pane-stack--full.side-pane-stack--right-docked", StringComparison.Ordinal);

        Assert.True(ruleStart >= 0, "The Tools pane must not stretch into a horizontal full-width pane when Backlog is hidden.");

        var nextRuleStart = css.IndexOf("\n.", ruleStart + 1, StringComparison.Ordinal);
        var rule = css[ruleStart..(nextRuleStart < 0 ? css.Length : nextRuleStart)];

        Assert.Contains("margin-left: auto;", rule, StringComparison.Ordinal);
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
