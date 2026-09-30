namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox pane is a flex column: header, the last run's line, then — when
/// the shell fills the sources slot — a tab strip over one panel, the queue or
/// the sources. Each panel takes the whole body the strip leaves, so the
/// sources are no longer a strip squeezed above the rows; these pin the rules
/// that make that so.
/// </summary>
public sealed class InboxSourcesLayoutTests
{
    [Fact]
    public void The_sources_panel_takes_the_body_and_scrolls_inside_it()
    {
        var rule = Rule(".inbox-pane__sources {");

        Assert.Contains("flex: 1 1 0;", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("max-height", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_panel_is_a_column_the_body_grows_into()
    {
        var rule = Rule(".inbox-pane__queue {");

        Assert.Contains("display: flex;", rule, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", rule, StringComparison.Ordinal);
        Assert.Contains("flex: 1 1 0;", rule, StringComparison.Ordinal);
    }

    /// <summary>The tab strip keeps the panel that is not chosen in the DOM,
    /// empty and hidden; a panel's own display rule would otherwise beat the
    /// attribute and leave it taking half the body.</summary>
    [Fact]
    public void A_hidden_panel_stays_hidden()
    {
        Assert.Contains(
            ".inbox-pane__queue[hidden],\n.inbox-pane__sources[hidden] {\n    display: none;",
            Css(),
            StringComparison.Ordinal);
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
    /// its floor; on a tab of their own there is nothing beneath them.</summary>
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
