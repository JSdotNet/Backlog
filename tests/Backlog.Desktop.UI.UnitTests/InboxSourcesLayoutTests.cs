namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox pane is a flex column: header, the last run's line, then the
/// body — the queue, or, when the header's Sources button is pressed, the
/// sources the shell put in the slot. The sources take the whole body the
/// header leaves rather than a strip squeezed above the rows; these pin the
/// rules that make that so.
/// </summary>
public sealed class InboxSourcesLayoutTests
{
    [Fact]
    public void The_sources_take_the_body_and_scroll_inside_it()
    {
        var rule = Rule(".inbox-pane__sources {");

        Assert.Contains("flex: 1 1 0;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("max-height", rule, StringComparison.Ordinal);
    }

    /// <summary>The tab strip is gone, and so are the rules that kept its
    /// hidden panel hidden.</summary>
    [Fact]
    public void The_tab_strip_rules_are_gone()
    {
        Assert.DoesNotContain(".inbox-pane__queue", Css(), StringComparison.Ordinal);
        Assert.DoesNotContain(".inbox-pane__tabs", Css(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_keeps_its_minimum_height()
    {
        var rule = Rule(".inbox-pane__body {\n    display: grid;");

        Assert.Contains("min-height: var(--inbox-queue-min-height);", rule, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 0;", rule, StringComparison.Ordinal);
        Assert.Contains("--inbox-queue-min-height:", Rule(":root {"), StringComparison.Ordinal);
    }

    /// <summary>The sources used to be capped so the queue beneath them kept
    /// its floor; with the body to themselves there is nothing beneath them.</summary>
    [Fact]
    public void The_sources_height_cap_is_gone()
    {
        Assert.DoesNotContain("--inbox-sources-max-height", Css(), StringComparison.Ordinal);
    }

    private static string Css() =>
        File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
            .Replace("\r\n", "\n");

    private static string Rule(string opening)
    {
        var css = Css();
        var start = css.IndexOf(opening, StringComparison.Ordinal);

        Assert.True(start >= 0, $"app.css should hold a rule opening with '{opening}'.");

        return css[start..css.IndexOf("\n}\n", start, StringComparison.Ordinal)];
    }
}
