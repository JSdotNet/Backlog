namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox pane is a flex column: header, the last run's line, the sources
/// slot, then the body with the queue. Opening the sources fold once left the
/// sources wrapper at its content height, and since the body was the only
/// child allowed to shrink it went to 0px — the queue could not be seen or
/// clicked. These pin the two rules that keep the queue standing.
/// </summary>
public sealed class InboxSourcesLayoutTests
{
    [Fact]
    public void Open_sources_scroll_inside_a_bounded_region()
    {
        var rule = Rule(".inbox-pane__sources {");

        Assert.Contains("max-height: var(--inbox-sources-max-height);", rule, StringComparison.Ordinal);
        Assert.Contains("min-height: 0;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-y: auto;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_queue_keeps_its_minimum_height_when_the_sources_are_open()
    {
        var rule = Rule(".inbox-pane__body {\n    display: grid;");

        Assert.Contains("min-height: var(--inbox-queue-min-height);", rule, StringComparison.Ordinal);
        // A content basis makes the body out-bid the folded sources line in the
        // shrink, squeezing that line to a pixel; it only grows from zero.
        Assert.Contains("flex: 1 1 0;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("min-height: 0;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_measurements_are_tokens_declared_on_the_root()
    {
        var root = Rule(":root {");

        Assert.Contains("--inbox-queue-min-height:", root, StringComparison.Ordinal);
        Assert.Contains("--inbox-sources-max-height:", root, StringComparison.Ordinal);
    }

    private static string Rule(string opening)
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
            .Replace("\r\n", "\n");
        var start = css.IndexOf(opening, StringComparison.Ordinal);

        Assert.True(start >= 0, $"app.css should hold a rule opening with '{opening}'.");

        return css[start..css.IndexOf("\n}\n", start, StringComparison.Ordinal)];
    }
}
