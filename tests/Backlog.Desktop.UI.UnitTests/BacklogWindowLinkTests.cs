using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Reading a <c>backlog://window/{surfaceId}?…</c> link (local ADR 0040 §7): the
/// surface it names and the query it carries, and nothing for any other text.
/// </summary>
public sealed class BacklogWindowLinkTests
{
    [Fact]
    public void A_window_link_names_its_surface_and_carries_its_query()
    {
        var link = BacklogWindowLink.Parse("backlog://window/tasks?plan=shell-pop-out-windows&entry=6742b6bf-34d4-4dad-825a-e63ec5e1064c");

        Assert.NotNull(link);
        Assert.Equal("tasks", link.SurfaceId);
        Assert.Equal(2, link.Query.Count);
        Assert.Equal("shell-pop-out-windows", link.Query["plan"]);
        Assert.Equal("6742b6bf-34d4-4dad-825a-e63ec5e1064c", link.Query["entry"]);
    }

    [Fact]
    public void Escaped_keys_and_values_are_read_unescaped()
    {
        var link = BacklogWindowLink.Parse("backlog://window/devbook?chapter=.devbook%2Farc42%2Fadr%2F0040-a.md%23history&scope=JSdotNet%2FBacklog");

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
        Assert.Equal(".devbook/arc42/adr/0040-a.md#history", link.Query["chapter"]);
        Assert.Equal("JSdotNet/Backlog", link.Query["scope"]);
    }

    [Fact]
    public void A_link_with_no_query_carries_an_empty_one()
    {
        var link = BacklogWindowLink.Parse("backlog://window/roadmap");

        Assert.NotNull(link);
        Assert.Equal("roadmap", link.SurfaceId);
        Assert.Empty(link.Query);
    }

    [Theory]
    [InlineData("backlog://window/roadmap/")]
    [InlineData("backlog://window/roadmap/?plan=x")]
    public void A_trailing_slash_after_the_surface_is_tolerated(string text)
    {
        Assert.Equal("roadmap", BacklogWindowLink.Parse(text)?.SurfaceId);
    }

    [Fact]
    public void The_scheme_and_the_window_segment_are_read_without_regard_to_case()
    {
        var link = BacklogWindowLink.Parse("BACKLOG://Window/devbook?chapter=a.md");

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
    }

    [Fact]
    public void The_surface_id_is_kept_as_written_for_the_catalog_to_judge()
    {
        Assert.Equal("proposals", BacklogWindowLink.Parse("backlog://window/proposals")?.SurfaceId);
        Assert.Equal("no-such-surface", BacklogWindowLink.Parse("backlog://window/no-such-surface")?.SurfaceId);
    }

    [Fact]
    public void Surrounding_whitespace_and_quotes_from_a_command_line_are_ignored()
    {
        Assert.Equal("tasks", BacklogWindowLink.Parse("  \"backlog://window/tasks?plan=a\"  ")?.SurfaceId);
    }

    [Fact]
    public void A_fragment_is_not_part_of_the_query()
    {
        var link = BacklogWindowLink.Parse("backlog://window/tasks?plan=a#ignored");

        Assert.NotNull(link);
        Assert.Equal("a", link.Query["plan"]);
        Assert.Single(link.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tasks")]
    [InlineData("https://window/tasks")]
    [InlineData("backlog:window/tasks")]
    [InlineData("backlog://")]
    [InlineData("backlog://window")]
    [InlineData("backlog://window/")]
    [InlineData("backlog://window/?plan=a")]
    [InlineData("backlog://window/tasks/extra")]
    [InlineData("backlog://devbook/JSdotNet-Backlog/.devbook/arc42/adr/0040.md")]
    [InlineData("backlog://windows/tasks")]
    public void Anything_but_a_window_link_is_no_link(string? text)
    {
        Assert.Null(BacklogWindowLink.Parse(text));
    }

    [Fact]
    public void The_first_window_link_among_command_line_arguments_is_found()
    {
        var link = BacklogWindowLink.Find(["C:\\Apps\\Backlog.Desktop.exe", "--flag", "backlog://window/goals", "backlog://window/tasks"]);

        Assert.Equal("goals", link?.SurfaceId);
    }

    [Fact]
    public void Arguments_without_a_window_link_find_none()
    {
        Assert.Null(BacklogWindowLink.Find(["Backlog.Desktop.exe", "----AppNotificationActivated:"]));
        Assert.Null(BacklogWindowLink.Find([]));
    }

    [Fact]
    public void A_link_written_by_the_window_route_reads_back_to_the_same_surface_and_query()
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["chapter"] = "notes/a b&c=d.md",
            ["scope"] = "brain",
        };

        var link = BacklogWindowLink.Parse("backlog:/" + SurfaceWindowRoute.PathFor("devbook", query));

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
        Assert.Equal(query, link.Query);
    }
}
