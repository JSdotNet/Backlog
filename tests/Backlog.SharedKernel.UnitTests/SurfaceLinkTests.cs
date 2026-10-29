namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The addresses a surface opens at (local ADR 0042 §6, §7): <c>/window/{surfaceId}</c>
/// and <c>backlog://window/{surfaceId}?…</c>, each key and value escaped, in the order
/// given; and a <c>backlog://window/…</c> link read back, the surface it names and the
/// query it carries, and nothing for any other text. Written and read in one place,
/// so one round trip proves the two agree.
/// </summary>
public class SurfaceLinkTests
{
    [Fact]
    public void A_surface_without_a_query_is_its_path_alone()
    {
        Assert.Equal("/window/roadmap", SurfaceLink.Path("roadmap"));
        Assert.Equal("backlog://window/roadmap", SurfaceLink.Protocol("roadmap"));
    }

    [Fact]
    public void The_protocol_link_carries_the_record_s_keys_in_order_and_escaped()
    {
        var link = SurfaceLink.Protocol("devbook", [new("chapter", ".devbook/arc42/a b.md"), new("scope", "JSdotNet/Backlog")]);

        Assert.Equal("backlog://window/devbook?chapter=.devbook%2Farc42%2Fa%20b.md&scope=JSdotNet%2FBacklog", link);
    }

    [Fact]
    public void The_protocol_link_is_a_uri_whose_host_is_window()
    {
        var uri = new Uri(SurfaceLink.Protocol("tasks", [new("plan", "shell-pop-out-windows"), new("entry", "8cb393df")]));

        Assert.Equal(SurfaceLink.Scheme, uri.Scheme);
        Assert.Equal("window", uri.Host);
        Assert.Equal("/tasks", uri.AbsolutePath);
        Assert.Equal("?plan=shell-pop-out-windows&entry=8cb393df", uri.Query);
    }

    [Fact]
    public void A_blank_surface_is_refused()
    {
        Assert.ThrowsAny<ArgumentException>(() => SurfaceLink.Path(" "));
    }

    [Fact]
    public void A_window_link_names_its_surface_and_carries_its_query()
    {
        var link = SurfaceLink.Parse("backlog://window/tasks?plan=shell-pop-out-windows&entry=6742b6bf-34d4-4dad-825a-e63ec5e1064c");

        Assert.NotNull(link);
        Assert.Equal("tasks", link.SurfaceId);
        Assert.Equal(2, link.Query.Count);
        Assert.Equal("shell-pop-out-windows", link.Query["plan"]);
        Assert.Equal("6742b6bf-34d4-4dad-825a-e63ec5e1064c", link.Query["entry"]);
    }

    [Fact]
    public void Escaped_keys_and_values_are_read_unescaped()
    {
        var link = SurfaceLink.Parse("backlog://window/devbook?chapter=.devbook%2Farc42%2Fadr%2F0040-a.md%23history&scope=JSdotNet%2FBacklog");

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
        Assert.Equal(".devbook/arc42/adr/0040-a.md#history", link.Query["chapter"]);
        Assert.Equal("JSdotNet/Backlog", link.Query["scope"]);
    }

    [Fact]
    public void A_link_with_no_query_carries_an_empty_one()
    {
        var link = SurfaceLink.Parse("backlog://window/roadmap");

        Assert.NotNull(link);
        Assert.Equal("roadmap", link.SurfaceId);
        Assert.Empty(link.Query);
    }

    [Theory]
    [InlineData("backlog://window/roadmap/")]
    [InlineData("backlog://window/roadmap/?plan=x")]
    public void A_trailing_slash_after_the_surface_is_tolerated(string text)
    {
        Assert.Equal("roadmap", SurfaceLink.Parse(text)?.SurfaceId);
    }

    [Fact]
    public void The_scheme_and_the_window_segment_are_read_without_regard_to_case()
    {
        var link = SurfaceLink.Parse("BACKLOG://Window/devbook?chapter=a.md");

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
    }

    [Fact]
    public void The_surface_id_is_kept_as_written_for_the_catalog_to_judge()
    {
        Assert.Equal("proposals", SurfaceLink.Parse("backlog://window/proposals")?.SurfaceId);
        Assert.Equal("no-such-surface", SurfaceLink.Parse("backlog://window/no-such-surface")?.SurfaceId);
    }

    [Fact]
    public void Surrounding_whitespace_and_quotes_from_a_command_line_are_ignored()
    {
        Assert.Equal("tasks", SurfaceLink.Parse("  \"backlog://window/tasks?plan=a\"  ")?.SurfaceId);
    }

    [Fact]
    public void A_fragment_is_not_part_of_the_query()
    {
        var link = SurfaceLink.Parse("backlog://window/tasks?plan=a#ignored");

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
        Assert.Null(SurfaceLink.Parse(text));
    }

    [Fact]
    public void The_first_window_link_among_command_line_arguments_is_found()
    {
        var link = SurfaceLink.Find(["C:\\Apps\\Backlog.Desktop.exe", "--flag", "backlog://window/goals", "backlog://window/tasks"]);

        Assert.Equal("goals", link?.SurfaceId);
    }

    [Fact]
    public void Arguments_without_a_window_link_find_none()
    {
        Assert.Null(SurfaceLink.Find(["Backlog.Desktop.exe", "----AppNotificationActivated:"]));
        Assert.Null(SurfaceLink.Find([]));
    }

    [Fact]
    public void A_link_the_builder_writes_parses_back_to_the_same_surface_and_query()
    {
        KeyValuePair<string, string>[] query =
        [
            new("plan", "shell-pop-out-windows"),
            new("entry", "8cb393df-7bf7-4573-b1c1-ba09882ca70d"),
            new("chapter", "arc42/adr/0042 a surface & its window+1.md"),
            new("scope", "JSdotNet/Backlog"),
        ];

        var link = SurfaceLink.Parse(SurfaceLink.Protocol("devbook", query));

        Assert.NotNull(link);
        Assert.Equal("devbook", link.SurfaceId);
        Assert.Equal(query.ToDictionary(pair => pair.Key, pair => pair.Value), link.Query);
    }
}
