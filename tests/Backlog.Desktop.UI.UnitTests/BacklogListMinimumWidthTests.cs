namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The backlog list's minimum is the width at which one of its rows is still whole.
/// <para>
/// The split's minimum is <c>--pane-min-width</c>, which this app aliases to
/// <c>--workspace-min-width</c> at 22rem — how little of the backlog the side pane
/// may leave standing. That number is about reading the list. It is not about
/// operating it, and the rows say so one step higher: below 38rem a row drops its
/// pencil, its copy button and its repository picker, because the four controls on
/// its trailing cluster come to most of a 38rem column.
/// </para>
/// <para>
/// With the two numbers apart, the default layout landed between them. At a 1280px
/// window with an entry open the split resolved to <c>576px 8px 640px</c>: the list
/// got 36rem, above the 22rem floor and below the 38rem step, so every control right
/// of the title went — with nothing dragged and no window resized. Opening an entry
/// was the whole gesture.
/// </para>
/// <para>
/// So this split raises the floor to clear the step it collides with. The controls
/// can still go: a reader who drags the panel out past what the list needs gets the
/// narrow row that width deserves, and a window too small to hold both halves stacks
/// them (<c>FilterBarLayoutTests</c>). What cannot happen is the layout choosing it
/// for them on first sight. <c>SplitPaneWidthTests</c> pins the library half — that
/// the grid honours this floor at all.
/// </para>
/// </summary>
public sealed class BacklogListMinimumWidthTests
{
    /// <summary>The container step the floor has to clear, found by the rule it
    /// carries rather than by its own number: the step and the floor are one
    /// decision, and a test holding its own copy of the step would pass while the
    /// two drifted apart. There are four steps on this container, so it is located
    /// by the declaration that defines it — the one that takes the row's pencil.</summary>
    private const string ClusterRule = ".task-item__edit,";

    private const string StepOpening = "@container backlog-list (max-width: ";

    [Fact]
    public void The_list_floor_clears_the_width_where_a_row_sheds_its_cluster()
    {
        var css = Css();

        var floor = Rem(Declaration(Block(css, ".backlog-split {"), "--pane-min-width"));
        var step = ClusterStepRem(css);

        Assert.True(
            floor > step,
            $"The backlog list's floor is {floor}rem and its rows shed their cluster at {step}rem, "
            + "so the split can hand the list a width its own rows cannot be operated at.");
    }

    /// <summary>
    /// And it is raised on the split rather than on the list.
    /// <para>
    /// The library's resizer reads <c>--pane-min-width</c> off the layout element it
    /// is dragging — the split itself — so a floor declared anywhere below it would
    /// hold the grid and not the drag, which is the same half-enforced minimum this
    /// whole fix is about. Declared here, the grid and the pointer read one value.
    /// </para>
    /// </summary>
    [Fact]
    public void The_floor_is_declared_where_the_resizer_reads_it()
    {
        var css = Css();

        Assert.Contains("--pane-min-width", Block(css, ".backlog-split {"), StringComparison.Ordinal);
        Assert.DoesNotContain("--pane-min-width", Block(css, ".backlog-list {"), StringComparison.Ordinal);
    }

    private static double ClusterStepRem(string css)
    {
        var rule = css.IndexOf(ClusterRule, StringComparison.Ordinal);
        Assert.True(rule >= 0, $"{ClusterRule} should exist.");

        var at = css.LastIndexOf(StepOpening, rule, StringComparison.Ordinal);
        Assert.True(at >= 0, "The row cluster should sit inside a container step.");

        var close = css.IndexOf(')', at);
        Assert.True(close >= 0, "The container step should be closed.");

        return Rem(css[(at + StepOpening.Length)..close]);
    }

    private static double Rem(string value)
    {
        var trimmed = value.Trim();

        Assert.EndsWith("rem", trimmed, StringComparison.Ordinal);

        return double.Parse(
            trimmed[..^3],
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Css() => File.ReadAllText(
        RepositoryRoot.File("src", "App", "Backlog.Desktop.UI", "wwwroot", "app.css"))
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

        return block[(at + property.Length + 1)..semicolon];
    }
}
