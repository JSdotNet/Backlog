namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A board column can be dropped on anywhere along the board's height, not only
/// where its cards are.
///
/// <para>The board lined its columns up at the top, so each column was only as
/// tall as its cards. A card low in a long column, carried across to a short one,
/// arrived beside that column's foot — over the board's own background, with no
/// <c>data-board-column</c> under the pointer — and the drop wrote nothing and
/// said nothing. At an ordinary window size the short column had scrolled out of
/// view entirely, so there was nowhere on screen the card could go
/// (task-views QA, scenario B3b).</para>
///
/// <para>Asserted against the stylesheet, for the reason
/// <see cref="TaskListWidthTests"/> gives: the markup is right, and the defect is
/// in what the layout engine does with it, which bUnit does not have. The
/// storybook's "A long column" story is the same check made with a pointer.</para>
/// </summary>
public sealed class TaskBoardColumnReachTests
{
    /// <summary>
    /// The regression. Every column is stretched to the height of the board's row,
    /// which is the tallest column's, so the space beside a long column's cards is
    /// the short column's own and a drop there lands on it.
    /// </summary>
    [Fact]
    public void Every_column_fills_the_height_of_the_board()
    {
        var board = Rule(".task-board");

        Assert.Matches(@"align-items:\s*stretch;", board);
        Assert.DoesNotMatch(@"align-items:\s*start;", board);
    }

    /// <summary>
    /// The column itself does not undo it: a column that set its own alignment or
    /// a fixed height would shrink back to its cards.
    /// </summary>
    [Fact]
    public void A_column_does_not_shrink_back_to_its_cards()
    {
        var column = Rule(".task-board__column");

        Assert.DoesNotMatch(@"align-self\s*:", column);
        Assert.DoesNotMatch(@"(^|[^-])height\s*:", column);
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
