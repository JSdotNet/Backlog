namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The two pieces of arithmetic a trace rests on, tested as arithmetic: which links
/// and things a dependency chain holds, and how arrows that would share a corridor
/// are drawn apart without moving an arrow that shares nothing.
/// </summary>
public sealed class RoadmapGeometryTraceTests
{
    private static readonly RoadmapGeometry Geometry = new(new RoadmapWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));

    private static RoadmapLink L(string from, string to) => new(from, to);

    [Fact]
    public void A_chain_reaches_everything_upstream_and_downstream_however_far()
    {
        var chain = RoadmapGeometry.Chain("b", [L("a", "b"), L("b", "c"), L("c", "d"), L("y", "z")]);

        Assert.Equal(["a", "b", "c", "d"], chain.Nodes.Order(StringComparer.Ordinal));
        Assert.Equal([L("a", "b"), L("b", "c"), L("c", "d")], chain.Links.OrderBy(link => link.FromId, StringComparer.Ordinal));
        Assert.True(chain.HasLinks);
    }

    [Fact]
    public void A_chain_holds_the_links_on_paths_through_the_traced_thing_and_no_others()
    {
        // x also feeds c, downstream of b: x neither waits on b nor is waited on by it.
        // a1 and a2 both lie upstream, and the link between them is on a path to b.
        var chain = RoadmapGeometry.Chain("b", [L("a1", "a2"), L("a2", "b"), L("b", "c"), L("x", "c")]);

        Assert.DoesNotContain("x", chain.Nodes);
        Assert.DoesNotContain(L("x", "c"), chain.Links);
        Assert.Contains(L("a1", "a2"), chain.Links);
    }

    [Fact]
    public void A_chain_through_a_loop_still_ends()
    {
        var chain = RoadmapGeometry.Chain("a", [L("a", "b"), L("b", "c"), L("c", "a")]);

        Assert.Equal(["a", "b", "c"], chain.Nodes.Order(StringComparer.Ordinal));
        Assert.Equal(3, chain.Links.Count);
    }

    [Fact]
    public void Something_with_no_links_is_a_chain_of_itself_alone()
    {
        var chain = RoadmapGeometry.Chain("lonely", [L("a", "b")]);

        Assert.Equal(["lonely"], chain.Nodes);
        Assert.False(chain.HasLinks);
    }

    [Fact]
    public void An_arrow_alone_in_its_corridor_keeps_exactly_the_plain_path()
    {
        var elbow = new RoadmapLinkEnds(1, 1.375, 6, 4.125);
        var detour = new RoadmapLinkEnds(6, 1.375, 3, 4.125);

        var paths = Geometry.LinkPaths([elbow, detour]);

        Assert.Equal(Geometry.LinkPath(1, 1.375, 6, 4.125), paths[0]);
        Assert.Equal(Geometry.LinkPath(6, 1.375, 3, 4.125), paths[1]);
    }

    [Fact]
    public void Elbows_turning_in_one_column_are_drawn_in_columns_of_their_own()
    {
        // Two sources on different rows, both waiting into one bar: the plain elbow
        // turns both down the same column, just ahead of the target.
        var first = new RoadmapLinkEnds(1, 1.375, 6, 6.875);
        var second = new RoadmapLinkEnds(2, 4.125, 6, 6.875);

        var paths = Geometry.LinkPaths([first, second]);

        Assert.Equal(Geometry.LinkPath(1, 1.375, 6, 6.875), paths[0]);
        Assert.NotEqual(Column(paths[0]), Column(paths[1]));

        // Stepped back toward its source, so it still turns between its two ends.
        var column = Column(paths[1]);
        Assert.InRange(column, second.StartX, second.EndX);
        Assert.Equal(Column(paths[0]) - RoadmapGeometry.CorridorStepRem, column, 3);
    }

    [Fact]
    public void Detours_returning_along_one_channel_are_drawn_along_channels_of_their_own()
    {
        // Three doubling-back arrows from row 0 into row 1: one channel, the gutter.
        RoadmapLinkEnds[] detours =
        [
            new(6, 1.375, 3, 4.125),
            new(7, 1.375, 2, 4.125),
            new(8, 1.375, 4, 4.125)
        ];

        var paths = Geometry.LinkPaths(detours);
        var channels = paths.Select(Channel).ToList();

        Assert.Equal(Geometry.LinkPath(6, 1.375, 3, 4.125), paths[0]);
        Assert.Equal(3, channels.Distinct().Count());

        // Still detours, and still inside the gutter between the two bars.
        var room = (Geometry.RowHeightRem - Geometry.BarHeightRem) / 2;
        Assert.All(paths, path => Assert.Equal(5, Commands(path)));
        Assert.All(channels, channel => Assert.InRange(channel, 2.75 - room, 2.75 + room));
    }

    [Fact]
    public void Detours_under_their_own_row_step_down_only_and_never_through_a_bar()
    {
        // Five arrows doubling back within row 0, their returns all under its bar over
        // overlapping stretches. Up from the plain channel is the bar they left.
        RoadmapLinkEnds[] detours =
        [
            new(6, 1.375, 3, 1.375),
            new(7, 1.375, 2, 1.375),
            new(8, 1.375, 4, 1.375),
            new(9, 1.375, 1, 1.375),
            new(10, 1.375, 2.5, 1.375)
        ];

        var channels = Geometry.LinkPaths(detours).Select(Channel).ToList();

        Assert.Equal(5, channels.Distinct().Count());

        // Below row 0's bar, and above row 1's.
        var barBottom = 1.375 + Geometry.BarHeightRem / 2;
        var nextBarTop = 1.375 + Geometry.RowHeightRem - Geometry.BarHeightRem / 2;
        Assert.All(channels, channel => Assert.InRange(channel, barBottom + 0.01, nextBarTop - 0.01));

        // Each further one lower than the one before.
        Assert.Equal(channels.Order(), channels);
    }

    [Fact]
    public void Runs_that_share_a_column_without_overlapping_keep_their_plain_paths()
    {
        // Rows 0→1 and rows 2→3, both turning just ahead of x = 6: one column, but the
        // two vertical runs never meet.
        var upper = new RoadmapLinkEnds(1, 1.375, 6, 4.125);
        var lower = new RoadmapLinkEnds(1, 6.875, 6, 9.625);

        var paths = Geometry.LinkPaths([upper, lower]);

        Assert.Equal(Geometry.LinkPath(1, 1.375, 6, 4.125), paths[0]);
        Assert.Equal(Geometry.LinkPath(1, 6.875, 6, 9.625), paths[1]);
    }

    [Fact]
    public void Returns_that_share_a_channel_without_overlapping_keep_their_plain_paths()
    {
        // Both double back between rows 0 and 1, along one gutter, months apart.
        var early = new RoadmapLinkEnds(6, 1.375, 3, 4.125);
        var late = new RoadmapLinkEnds(30, 1.375, 27, 4.125);

        var paths = Geometry.LinkPaths([early, late]);

        Assert.Equal(Geometry.LinkPath(30, 1.375, 27, 4.125), paths[1]);
    }

    [Fact]
    public void Elbows_with_little_room_take_every_free_column_before_any_two_share_one()
    {
        // Barely room for an elbow: the plain column sits at the source's stand-off,
        // so stepping back runs out after one step, and the next lane steps forward.
        RoadmapLinkEnds[] crowded =
        [
            new(0, 1.375, 1, 12.375),
            new(0, 4.125, 1, 12.375),
            new(0, 9.625, 1, 12.375)
        ];

        var columns = Geometry.LinkPaths(crowded).Select(Column).ToList();

        Assert.Equal(3, columns.Distinct().Count());
        Assert.All(columns, column => Assert.InRange(column, 0.0, 1.0));
    }

    /// <summary>The x of an elbow's one vertical run: "M x y H corner V y H x".</summary>
    private static double Column(string path) => Number(path.Split(' ')[4]);

    /// <summary>The y a detour returns along: "M x y H out V channel H back V y H x".</summary>
    private static double Channel(string path) => Number(path.Split(' ')[6]);

    private static int Commands(string path) => path.Split(' ').Count(token => token is "H" or "V");

    private static double Number(string token) => double.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
}
