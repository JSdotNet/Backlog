using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A band's own content in the sidebar — something of the host's under the band's
/// name, such as a control that belongs to the band rather than to one of its rows.
/// Pinned here: it is drawn only where a band asks for it, in the sidebar's one
/// column; no row with a name is ever under it, because empty rows are put in before
/// the band's first named row — after its unnamed lane rows, or at the top — until it
/// starts at the rows the band asked for; the content is reachable by a screen reader
/// while the names stay hidden; and a chart with no content renders exactly as it did.
/// </summary>
public sealed class RoadmapTimelineGroupContentTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    /// <summary>
    /// Four kinds of band:
    /// <list type="bullet">
    /// <item>web — one unnamed lane, asking for three rows: nothing named to push down,
    /// so the room goes after the lane;</item>
    /// <item>backlog — an unnamed lane, then two named ones, asking for two: the room
    /// goes between the unnamed lane and the first named one;</item>
    /// <item>docs — only a named lane, asking for two: the room goes at the top;</item>
    /// <item>dates — milestones, asking for nothing.</item>
    /// </list>
    /// </summary>
    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("web", "web", [new RoadmapRow("web-lane", string.Empty)], ContentRows: 3),
        new("backlog", "backlog",
            [
                new RoadmapRow("backlog-lane", string.Empty),
                new RoadmapRow("platform", "platform"),
                new RoadmapRow("tools", "tools")
            ],
            ContentRows: 2),
        new("docs", "docs", [new RoadmapRow("api", "api")], ContentRows: 2),
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)])
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "platform", "Alpha", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16)),
        new("beta", "web-lane", "Beta", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16))
    ];

    private static readonly RenderFragment<RoadmapGroup> Content = group => builder =>
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "data-testid", $"content-{group.Id}");
        builder.AddContent(3, $"Pace for {group.Title}");
        builder.CloseElement();
    };

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        RenderFragment<RoadmapGroup>? content = null,
        IReadOnlyList<RoadmapGroup>? groups = null,
        Action<ComponentParameterCollectionBuilder<RoadmapTimeline>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, groups ?? Plan)
                .Add(timeline => timeline.Bars, Work)
                .Add(timeline => timeline.Window, Q1)
                .Add(timeline => timeline.TestId, "rm");

            if (content is not null) parameters.Add(timeline => timeline.GroupContent, content);

            extra?.Invoke(parameters);
        });
    }

    /// <summary>The track's rows, top to bottom, as the test ids they wear.</summary>
    private static List<string> TrackRows(IRenderedComponent<RoadmapTimeline> view) =>
        [.. view.FindAll(".roadmap-timeline__body > .roadmap-timeline__row").Select(row => row.GetAttribute("data-testid")!)];

    /// <summary>A band's names, top to bottom, one per row: blank for a row with none.</summary>
    private static List<string> Names(IRenderedComponent<RoadmapTimeline> view, string group) =>
        [.. view.FindAll($"[data-testid='rm-group-{group}'] .roadmap-timeline__row-name")
            .Select(row => row.QuerySelector(".roadmap-timeline__lane-name")?.TextContent ?? string.Empty)];

    [Fact]
    public void Content_is_drawn_under_the_name_of_each_band_that_asked_for_it_and_no_other()
    {
        using var context = new BunitContext();

        var view = Chart(context, Content);

        Assert.Equal("Pace for web", view.Find("[data-testid='rm-group-web-content'] [data-testid='content-web']").TextContent);
        Assert.NotNull(view.Find("[data-testid='rm-group-backlog-content'] [data-testid='content-backlog']"));
        Assert.Empty(view.FindAll("[data-testid='rm-group-dates-content']"));
        Assert.Empty(view.FindAll("[data-testid='content-dates']"));

        // One column, as without content: the names and the content share the band.
        Assert.Empty(view.FindAll(".roadmap-timeline__group-slot"));
        var band = view.Find("[data-testid='rm-group-web']");
        Assert.Equal("web", band.QuerySelector(".roadmap-timeline__group-name")!.TextContent);
        Assert.Contains("roadmap-timeline__group--slotted", band.ClassName!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_content_starts_under_the_name_and_ends_with_the_rows_the_band_asked_for()
    {
        using var context = new BunitContext();

        var view = Chart(context, Content);

        // Rows are 2.75rem: the name is centred on the first, so the content starts
        // 1.375 + 0.75 down, and runs to the end of the band's third row.
        Assert.Equal(
            "top: 2.125rem; height: 6.125rem",
            view.Find("[data-testid='rm-group-web-content']").GetAttribute("style"));
    }

    [Fact]
    public void Room_goes_before_the_first_named_row_so_nothing_named_is_under_the_content()
    {
        using var context = new BunitContext();

        var view = Chart(context, Content);

        Assert.Equal(
            [
                // web: its unnamed lane beside the content, then the room.
                "rm-row-web-lane", "rm-padding-web--padding--0", "rm-padding-web--padding--1",
                // backlog: its unnamed lane, one row of room, then its named lanes from row 2.
                "rm-row-backlog-lane", "rm-padding-backlog--padding--0", "rm-row-platform", "rm-row-tools",
                // docs: no unnamed lane, so all the room at the top.
                "rm-padding-docs--padding--0", "rm-padding-docs--padding--1", "rm-row-api",
                "rm-row-moments"
            ],
            TrackRows(view));

        // The sidebar keeps pace row for row, and each band's first name sits at the
        // row it asked for or below.
        Assert.Equal(TrackRows(view).Count, view.FindAll(".roadmap-timeline__row-name").Count);
        Assert.Equal(["", "", ""], Names(view, "web"));
        Assert.Equal(["", "", "platform", "tools"], Names(view, "backlog"));
        Assert.Equal(["", "", "api"], Names(view, "docs"));
    }

    [Fact]
    public void A_band_with_enough_unnamed_rows_gets_no_room()
    {
        using var context = new BunitContext();

        IReadOnlyList<RoadmapGroup> groups =
        [
            new("web", "web",
                [new RoadmapRow("one", string.Empty), new RoadmapRow("two", string.Empty), new RoadmapRow("named", "named")],
                ContentRows: 2)
        ];

        var view = Chart(context, Content, groups);

        Assert.Equal(["rm-row-one", "rm-row-two", "rm-row-named"], TrackRows(view));
    }

    [Fact]
    public void A_padding_row_is_inert_and_says_nothing()
    {
        using var context = new BunitContext();

        var view = Chart(context, Content);

        var padding = view.Find("[data-testid='rm-padding-backlog--padding--0']");
        Assert.Equal("true", padding.GetAttribute("aria-hidden"));
        Assert.Null(padding.GetAttribute("role"));
        Assert.Empty(padding.Children);
    }

    [Fact]
    public void The_content_is_reachable_and_named_for_its_band_while_the_names_stay_hidden()
    {
        using var context = new BunitContext();

        var view = Chart(context, Content);

        var sidebar = view.Find(".roadmap-timeline__sidebar");
        Assert.Null(sidebar.GetAttribute("aria-hidden"));

        var content = view.Find("[data-testid='rm-group-web-content']");
        Assert.Equal("group", content.GetAttribute("role"));
        Assert.Equal("web", content.GetAttribute("aria-label"));

        // Nothing between the content and the page is hidden.
        for (var element = view.Find("[data-testid='content-web']"); element is not null; element = element.ParentElement)
        {
            Assert.Null(element.GetAttribute("aria-hidden"));
        }

        // Every column of names is, band names included.
        Assert.All(
            view.FindAll(".roadmap-timeline__group-rows"),
            rows => Assert.Equal("true", rows.GetAttribute("aria-hidden")));
        Assert.All(
            view.FindAll(".roadmap-timeline__group-name"),
            name => Assert.NotNull(name.Closest("[aria-hidden='true']")));
    }

    [Fact]
    public void Without_content_the_chart_renders_exactly_as_it_did()
    {
        using var plain = new BunitContext();
        using var unused = new BunitContext();

        // No GroupContent at all, and a GroupContent no band asked for: the same
        // markup either way, and the old single hidden column.
        var groups = Plan.Select(group => group with { ContentRows = 0 }).ToList();
        var without = Chart(plain, groups: groups);
        var ignored = Chart(unused, Content, groups);

        Assert.Equal("true", without.Find(".roadmap-timeline__sidebar").GetAttribute("aria-hidden"));
        Assert.Empty(without.FindAll(".roadmap-timeline__sidebar--slotted"));
        Assert.Empty(without.FindAll(".roadmap-timeline__row--padding"));
        Assert.Equal(
            Stripped(without.Find(".roadmap-timeline__sidebar").OuterHtml),
            Stripped(ignored.Find(".roadmap-timeline__sidebar").OuterHtml));

        // A band asking for rows with no content to fill them is not padded either.
        using var asked = new BunitContext();
        Assert.Empty(Chart(asked, groups: Plan).FindAll(".roadmap-timeline__row--padding"));
    }

    [Fact]
    public void The_keyboard_steps_over_padding()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, Content, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        // beta is on web's lane; the next row down a bar can land on is backlog's
        // unnamed lane, past web's two rows of room.
        var bar = view.Find("[data-testid='rm-bar-beta'] .roadmap-bar__body");
        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar = view.Find("[data-testid='rm-bar-beta'] .roadmap-bar__body");
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        bar = view.Find("[data-testid='rm-bar-beta'] .roadmap-bar__body");
        bar.KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.Equal("backlog-lane", reported?.RowId);
    }

    [Theory]
    [InlineData(-1, "backlog-lane")] // backlog's room, under its unnamed lane
    [InlineData(-3, "web-lane")]     // web's room, under web's lane
    public async Task A_pointer_dropping_a_bar_into_padding_lands_it_on_the_nearest_lane_of_that_band(
        int rows,
        string expected)
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, Content, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        // alpha sits on platform, row 5.
        await view.InvokeAsync(() => view.Instance.DragBegin("alpha", "move"));
        await view.InvokeAsync(() => view.Instance.DragPreview(0, rows));
        await view.InvokeAsync(view.Instance.DragCommit);

        Assert.Equal(expected, reported?.RowId);
    }

    [Fact]
    public async Task A_bar_dropped_into_room_at_the_top_of_a_band_lands_on_the_lane_below_it()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, Content, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        // alpha on platform, row 5; two rows down past tools is docs' first padding
        // row, which has no lane above it in its band — so it lands on api below.
        await view.InvokeAsync(() => view.Instance.DragBegin("alpha", "move"));
        await view.InvokeAsync(() => view.Instance.DragPreview(0, 2));
        await view.InvokeAsync(view.Instance.DragCommit);

        Assert.Equal("api", reported?.RowId);
    }

    /// <summary>The markup without the per-instance ids a second render mints anew.</summary>
    private static string Stripped(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "[0-9a-f]{32}", string.Empty);
}
