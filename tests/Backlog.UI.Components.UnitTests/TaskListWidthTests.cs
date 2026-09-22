namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A task list is exactly as wide as the column it is in, whatever its rows would
/// like to be.
///
/// <para>The list is a one-column grid, and it had that column implicitly — an
/// <c>auto</c> track, whose base size is the widest row's min-content. A row's
/// min-content includes its title in full, because the title does not wrap; and
/// <c>min-width: 0</c> on the body and the line, which is what lets the title give
/// up room, only lets them shrink once the row is narrower than they are. The row
/// never was: the track grew to fit it, every row stretched to the track, and the
/// split's half — the nearest thing that scrolls — grew a horizontal scrollbar.
/// One entry titled with a URL was enough to do it to the whole list, at a width
/// the container step that drops the row's trailing cluster does not even reach,
/// because the step measures the column and the column was still the right size.
/// It was the rows that were not.</para>
///
/// <para>Asserted against the stylesheet, for the reason
/// <see cref="TaskRenameFieldUnderlineTests"/> gives: the markup is right, and the
/// whole of the defect is in what the layout engine does with it, which bUnit
/// does not have.</para>
/// </summary>
public sealed class TaskListWidthTests
{
    /// <summary>
    /// The regression. The track is declared with a floor of zero, so no row can
    /// widen it — a row wider than the column is constrained by it, and the title
    /// ellipses the way it was always meant to.
    /// </summary>
    [Fact]
    public void A_row_cannot_widen_the_list_past_its_column()
    {
        var list = Rule(".task-list");

        Assert.Matches(@"grid-template-columns:\s*minmax\(0,\s*1fr\)", list);
    }

    /// <summary>
    /// The list is not made a scroll container to get there. The bulk bar above the
    /// rows and the add-entry row among them are sticky against the split's half,
    /// and a scroll container here would be the nearer one — and one that never
    /// scrolls, so both would scroll straight out of view stuck to a box as tall
    /// as the rows. <c>app.css</c> says the same over <c>.backlog-list</c>.
    /// </summary>
    [Fact]
    public void The_list_is_not_a_scroll_container()
    {
        var list = Rule(".task-list");

        Assert.DoesNotMatch(@"overflow(-x|-y)?\s*:\s*(auto|scroll|hidden)", list);
    }

    /// <summary>
    /// Once a row is held to its column, what is on the row has to fit it or give
    /// way, and the one line that could not was the names a blocked row is waiting
    /// for — a sentence of other rows' titles, on a line that does not wrap. It
    /// truncates now, the way the title above it does; the whole name is still the
    /// control's accessible name, and the row it names is one click away.
    /// </summary>
    [Fact]
    public void The_names_a_blocked_row_waits_for_give_up_room_like_the_title()
    {
        var waiting = Rule(".task-item__waiting");

        Assert.Contains("min-width: 0;", waiting, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", waiting, StringComparison.Ordinal);
        Assert.Contains("text-overflow: ellipsis;", waiting, StringComparison.Ordinal);

        // The detail that holds it is a flex item on a wrapping line, and a flex
        // item will not shrink below its content unless told it may.
        Assert.Contains("min-width: 0;", Rule(".task-item__detail--blocked"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The name that can be followed is a button, and a button is an atomic inline
    /// whatever <c>display</c> says: the line clips it whole and puts no ellipsis
    /// inside it. Verified in the harness — the sentence stopped at the pencil with
    /// no sign it had been cut. So the button truncates its own text, against the
    /// width of the line it stands on.
    /// </summary>
    [Fact]
    public void A_name_that_can_be_followed_truncates_its_own_text()
    {
        var dependency = Rule(".task-item__dependency");

        Assert.Contains("display: inline-block;", dependency, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%;", dependency, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", dependency, StringComparison.Ordinal);
        Assert.Contains("text-overflow: ellipsis;", dependency, StringComparison.Ordinal);

        // A hidden overflow moves an inline-block's baseline to its bottom edge,
        // which would lift the name off the line it is a word in.
        Assert.Contains("vertical-align: top;", dependency, StringComparison.Ordinal);
    }

    /// <summary>The rule whose selector list starts a line with <paramref name="selector"/>
    /// and ends there, braces matched so a nested block cannot close it early — the
    /// same helper <see cref="TaskRenameFieldUnderlineTests"/> carries, for the same
    /// reason: each selector here is the tail of longer ones too.</summary>
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
