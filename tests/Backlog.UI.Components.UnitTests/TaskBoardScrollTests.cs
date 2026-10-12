namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A board scrolls sideways only, and each column scrolls its own cards once.
///
/// <para>The board's <c>overflow-x: auto</c> made it a vertical scroller as well,
/// since a box that scrolls one way computes <c>auto</c> for the other. In the
/// Tasks pane the board sits in <c>.backlog-list__scroll</c>, a flex column, where
/// a scroller's automatic minimum height is zero: the board shrank to the pane and
/// scrolled itself, and its scrollbar stood beside the pane's — two vertical
/// scrollbars for one long column (task-views sign-off). Measured in the
/// storybook: a 426px board over 1520px of cards.</para>
///
/// <para>Asserted against the stylesheet, for the reason
/// <see cref="TaskBoardColumnReachTests"/> gives: bUnit has no layout engine. The
/// storybook's "A long column" story is the same check with a scrollbar to look
/// at.</para>
/// </summary>
public sealed class TaskBoardScrollTests
{
    /// <summary>The regression. The board never scrolls vertically, fills the box
    /// it is given, and lays its columns in one row that the box's height bounds.</summary>
    [Fact]
    public void The_board_scrolls_sideways_and_never_down()
    {
        var board = Rule(".task-board");

        Assert.Matches(@"overflow-x:\s*auto;", board);
        Assert.Matches(@"overflow-y:\s*hidden;", board);
        Assert.Matches(@"grid-template-rows:\s*minmax\(0,\s*1fr\);", board);
        // A floor rather than zero: a short pane scrolls to the board's foot
        // instead of clipping a column's head and "+ New entry".
        Assert.Matches(@"min-height:\s*16rem;", board);
    }

    /// <summary>Each column's card stack is that column's one vertical scroller,
    /// and the column lets it shrink to do so.</summary>
    [Fact]
    public void A_columns_cards_are_its_one_vertical_scroller()
    {
        var cards = Rule(".task-board__cards");
        var column = Rule(".task-board__column");

        Assert.Matches(@"overflow-y:\s*auto;", cards);
        Assert.Matches(@"min-height:\s*0;", cards);
        Assert.Matches(@"min-height:\s*0;", column);
        Assert.DoesNotMatch(@"overflow(-y)?:\s*(auto|scroll)", column);
    }

    /// <summary>The drag's motion — the lifted card's tilt, the slot unfolding, the
    /// card settling — is all switched off under reduced motion.</summary>
    [Fact]
    public void The_drag_motion_stops_under_reduced_motion()
    {
        var css = Css();
        var at = css.IndexOf("@media (prefers-reduced-motion: reduce) {\n    .task-board__slot {", StringComparison.Ordinal);
        Assert.True(at >= 0, "components.css has no reduced-motion block for the board's drag.");
        var reduced = css[at..];
        reduced = reduced[..reduced.IndexOf("\n}\n", StringComparison.Ordinal)];

        Assert.Contains("animation: none;", reduced, StringComparison.Ordinal);
        Assert.Matches(@"\.task-card--lifted \{\s*transform: translate3d\(var\(--lift-x, 0\), var\(--lift-y, 0\), 0\);", reduced);
        Assert.Matches(@"\.task-card--lifted\.task-card--settling \{\s*transition: none;", reduced);
    }

    /// <summary>The rule whose selector list starts a line with <paramref name="selector"/>,
    /// braces matched — the helper <see cref="TaskListWidthTests"/> carries.</summary>
    private static string Rule(string selector)
    {
        var css = Css();

        var start = css.IndexOf("\n" + selector + " {", StringComparison.Ordinal) + 1;
        Assert.True(start > 0, $"components.css has no rule for {selector}.");

        var depth = 0;

        for (var index = css.IndexOf('{', start); index >= 0 && index < css.Length; index++)
        {
            if (css[index] == '{')
            {
                depth++;
            }
            else if (css[index] == '}' && --depth == 0)
            {
                return css[start..(index + 1)];
            }
        }

        Assert.Fail($"The rule for {selector} in components.css is never closed.");
        return string.Empty;
    }

    private static string Css() => File.ReadAllText(RepositoryRoot.File(
        "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");
}
