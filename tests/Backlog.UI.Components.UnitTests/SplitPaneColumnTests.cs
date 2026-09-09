namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The split's three parts name their own columns.
/// <para>
/// The template declares three tracks and the component renders three children in
/// that order, so auto-placement got them right for as long as all three were
/// there. It is the "all three" that is not the split's to assume: the separator
/// wears <c>.pane-resizer</c>, the class the app's own knowledge layout uses for
/// its edge, and a stylesheet outside this library hid every element carrying that
/// class at its own stacking width. A <c>display: none</c> child is not a grid
/// item, so the split lost the middle of its three and auto-placement slid the
/// trailing pane one column left — into the 8px track meant for the separator,
/// with the pane's real track left standing empty at the far edge.
/// </para>
/// <para>
/// Measured, at a 1024x900 window with an entry open: <c>624px 172px 172px</c>.
/// The list at its floor, the entry panel at 172px against 224px of content, and a
/// 172px column of nothing beside it. Nothing in the split was wrong; it had simply
/// been counted rather than told.
/// </para>
/// <para>
/// So each part is told. A pane placed by name stays in its own track whatever
/// happens to the separator, and the failure that is left when someone hides one is
/// a missing handle rather than a broken layout. The app also stopped hiding it —
/// <c>PaneResizerScopeTests</c> — and these two are deliberately both: one keeps
/// this out of the layout, the other keeps it out of the whole class of layout.
/// </para>
/// </summary>
public sealed class SplitPaneColumnTests
{
    [Theory]
    [InlineData(".split-pane__start {", "1")]
    [InlineData(".split-pane__separator {", "2")]
    [InlineData(".split-pane__end {", "3")]
    public void Each_part_is_placed_in_the_track_the_template_drew_for_it(string selector, string column)
    {
        var css = Css();

        Assert.Equal(column, Declaration(Block(css, selector), "grid-column"));
    }

    /// <summary>
    /// And the placement holds for both anchors, because the anchor swaps the two
    /// track *sizes* and never the order the component renders its children in. A
    /// second set of placements keyed off <c>.split-pane--end</c> would be the same
    /// drift the templates were already paired to prevent.
    /// </summary>
    [Fact]
    public void The_anchor_does_not_replace_the_placement()
    {
        var css = Css();

        Assert.DoesNotContain("grid-column", Block(css, ".split-pane--end {"), StringComparison.Ordinal);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"))
        .Replace("\r\n", "\n");

    private static string Block(string css, string opening)
    {
        var start = css.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{opening} should exist.");

        var close = css.IndexOf('}', start);
        Assert.True(close >= 0, $"{opening} should be closed.");

        return css[start..close];
    }

    private static string Declaration(string block, string property)
    {
        var at = block.IndexOf(property + ":", StringComparison.Ordinal);
        Assert.True(at >= 0, $"{property} should be declared.");

        var semicolon = block.IndexOf(';', at);
        Assert.True(semicolon >= 0, $"{property} should end in a semicolon.");

        return block[(at + property.Length + 1)..semicolon].Trim();
    }
}
