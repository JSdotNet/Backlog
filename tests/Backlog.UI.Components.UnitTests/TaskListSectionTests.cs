namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="TaskListView.SectionOf"/>: a heading wherever the host's group changes
/// down the open rows, and none at all while the host hands no grouping.
/// </summary>
public sealed class TaskListSectionTests
{
    private static readonly IReadOnlyList<TaskRow> Rows =
    [
        new("a", "Call the plumber"),
        new("b", "Ring the bank"),
        new("c", "Print the forms"),
        new("d", "Read the lease")
    ];

    private static readonly Dictionary<string, string?> Groups = new()
    {
        ["a"] = "@calls",
        ["b"] = "@calls",
        ["c"] = "@pc",
        ["d"] = "No context"
    };

    [Fact]
    public void A_heading_opens_each_group_in_the_order_the_rows_come()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(t => t.Tasks, Rows)
            .Add(t => t.SectionOf, task => Groups[task.Id])
            .Add(t => t.TestId, "list"));

        var headings = view.FindAll("[data-testid='list-section']");

        Assert.Equal(["@calls", "@pc", "No context"], headings.Select(heading => heading.TextContent));
        Assert.All(headings, heading => Assert.Equal("presentation", heading.GetAttribute("role")));
    }

    [Fact]
    public void With_no_grouping_there_is_no_heading()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(t => t.Tasks, Rows)
            .Add(t => t.TestId, "list"));

        Assert.Empty(view.FindAll("[data-testid='list-section']"));
    }

    /// <summary>A row with no group sits under the heading before it, and a group the
    /// host splits draws its heading once: a key appears once among the rows.</summary>
    [Fact]
    public void A_split_or_missing_group_draws_no_second_heading()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var groups = new Dictionary<string, string?> { ["a"] = "@calls", ["b"] = null, ["c"] = "@pc", ["d"] = "@calls" };

        var view = context.Render<TaskListView>(p => p
            .Add(t => t.Tasks, Rows)
            .Add(t => t.SectionOf, task => groups[task.Id])
            .Add(t => t.TestId, "list"));

        Assert.Equal(
            ["@calls", "@pc"],
            view.FindAll("[data-testid='list-section']").Select(heading => heading.TextContent));
    }
}
