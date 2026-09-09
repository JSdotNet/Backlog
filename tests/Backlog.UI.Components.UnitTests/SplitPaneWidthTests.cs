namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The split's grid honours the same minimum its resizer does.
/// <para>
/// <c>--pane-min-width</c> is declared in <c>components.css</c> as "how little room
/// the pane resizer must leave the content beside it", and the pointer drag has
/// always clamped to it. The grid never did: the fixed pane's declared width was a
/// plain track length, so it was satisfied first and the flexible pane took
/// whatever was left — down to nothing. A minimum enforced against a drag and not
/// against the layout is not a minimum; it only holds while the reader is the one
/// making the pane wider.
/// </para>
/// <para>
/// What that cost, measured: at a 1280px window with an entry open, the backlog's
/// split resolved to <c>576px 8px 640px</c> and the list half came out 36rem — two
/// below the 38rem step at which a row drops its pencil, its copy button and its
/// repository picker. The reader had dragged nothing; opening an entry was enough.
/// <c>BacklogListMinimumWidthTests</c> pins the app's half of that.
/// </para>
/// <para>
/// The grid template lives in the stylesheet rather than in the component, so this
/// reads the stylesheet. <c>SplitPaneTests</c> covers everything about the split
/// that is expressible in markup.
/// </para>
/// </summary>
public sealed class SplitPaneWidthTests
{
    /// <summary>The flexible pane's floor, as both templates must spell it.
    /// <c>min(100%, …)</c> so a split narrower than the floor keeps the floor from
    /// pushing the grid past its own box — a minimum may starve the pane beside it,
    /// never the window.</summary>
    private const string FlexibleFloor = "minmax(min(100%, var(--pane-min-width, 0px)), 1fr)";

    /// <summary>And the fixed pane yields to it. A bare <c>var(--split-pane-fixed)</c>
    /// track is a length the grid owes before it owes the floor anything.</summary>
    private const string YieldingFixed = "minmax(0, var(--split-pane-fixed, 36rem))";

    [Fact]
    public void The_fixed_pane_yields_before_the_flexible_one_starves()
    {
        var css = Css();

        foreach (var selector in new[] { ".split-pane {", ".split-pane--end {" })
        {
            var block = Block(css, selector);
            var columns = Declaration(block, "grid-template-columns");

            Assert.Contains(FlexibleFloor, columns, StringComparison.Ordinal);
            Assert.Contains(YieldingFixed, columns, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two templates are the same decision read from opposite ends, so the fixed
    /// pane is first in one and last in the other and nothing else moves. They drifted
    /// once already — a grid whose fixed column was last while the anchor attribute
    /// still said "start" ran the drag backwards — which is why they are asserted as a
    /// pair rather than one at a time.
    /// </summary>
    [Fact]
    public void The_anchor_only_swaps_which_end_the_fixed_pane_is_on()
    {
        var css = Css();

        var start = Declaration(Block(css, ".split-pane {"), "grid-template-columns");
        var end = Declaration(Block(css, ".split-pane--end {"), "grid-template-columns");

        Assert.Equal($"{YieldingFixed} auto {FlexibleFloor}", start);
        Assert.Equal($"{FlexibleFloor} auto {YieldingFixed}", end);
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
