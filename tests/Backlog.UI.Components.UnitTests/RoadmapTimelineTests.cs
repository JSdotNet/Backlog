using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A chart drawn from dates is two things worth pinning. The first is the
/// arithmetic — a date becomes a distance, and every bar, rule and arrow on
/// screen is that one calculation repeated — so it is tested as numbers rather
/// than read back out of a rendered stylesheet. The second is everything the
/// timeline declines to do: invent a colour, snap the dates of a plan the reader
/// only looked at, draw an arrow to something that is not on screen, or drop a
/// span on a row that has no duration to give it.
/// </summary>
public sealed class RoadmapTimelineTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    /// <summary>A quarter drawn at its own nominal length, which makes a day
    /// exactly one rem. Every measurement below is then a day count, so a wrong
    /// one names the day it is out by instead of arriving as a stray decimal.</summary>
    private static readonly RoadmapGeometry DayPerRem = new(Q1, RoadmapWindow.NominalQuarterDays);

    // 1 January 2026 is a Thursday, so the Mondays are the 5th, 12th, 19th and
    // 26th. Every date below is one of those or a stated number of days off one.
    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build"), new RoadmapRow("ship", "Ship")], "#3366ff"),
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)])
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "build", "Alpha", On(1, 5), On(1, 16),
            Facets: [new RoadmapFacet("Tag", "design"), new RoadmapFacet("Repo", "ui")]),
        new("beta", "ship", "Beta", On(1, 19), On(1, 30), Shade: 2,
            Facets: [new RoadmapFacet("Tag", "infra")]),
        new("gamma", "build", "Gamma", On(2, 2), On(2, 13))
    ];

    private static readonly IReadOnlyList<RoadmapMilestone> Moments =
    [
        new("launch", "moments", "Launch", On(1, 26))
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        IReadOnlyList<RoadmapBar>? bars = null,
        Action<ComponentParameterCollectionBuilder<RoadmapTimeline>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, Plan)
                .Add(timeline => timeline.Bars, bars ?? Work)
                .Add(timeline => timeline.Milestones, Moments)
                .Add(timeline => timeline.Window, Q1)
                .Add(timeline => timeline.TestId, "rm");

            extra?.Invoke(parameters);
        });
    }

    /// <summary>Which bars are on the chart, by id and in id order — the chart
    /// draws them row by row, and which row a bar sits on is pinned separately
    /// from which bars survive a filter.</summary>
    private static IEnumerable<string> DrawnBars(IRenderedComponent<RoadmapTimeline> view) =>
        view.FindAll(".roadmap-bar")
            .Select(bar => bar.GetAttribute("data-roadmap-bar")!)
            .Order(StringComparer.Ordinal);

    private static string Announcement(IRenderedComponent<RoadmapTimeline> view) =>
        view.Find("[data-testid='rm-announcement']").TextContent;

    // --- Where a date lands ---------------------------------------------------

    [Fact]
    public void The_first_day_of_the_window_sits_hard_against_the_left_edge()
    {
        Assert.Equal(0, DayPerRem.XFor(Q1.Start));
        Assert.Equal(0, new RoadmapGeometry(Q1).XFor(Q1.Start));
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(1, 11, 10)]
    [InlineData(2, 1, 31)]
    [InlineData(3, 31, 89)]
    public void A_date_is_placed_by_the_number_of_days_it_is_into_the_window(int month, int day, double expected) =>
        Assert.Equal(expected, DayPerRem.XFor(On(month, day)));

    [Fact]
    public void A_span_is_as_wide_as_the_days_it_covers_counting_both_ends()
    {
        // The one that has to be said out loud: a bar from the 1st to the 1st is
        // a day of work, not nothing. An exclusive end would draw it as a bar
        // with no width, and every bar on the chart a day short.
        Assert.Equal(1, DayPerRem.WidthFor(On(1, 1), On(1, 1)));
        Assert.Equal(7, DayPerRem.WidthFor(On(1, 5), On(1, 11)));
        Assert.Equal(12, DayPerRem.WidthFor(On(1, 5), On(1, 16)));
    }

    [Fact]
    public void A_span_too_short_to_draw_is_still_drawn()
    {
        // At a readable zoom a single day is a fifth of a rem, which is a bar
        // nobody can see and nobody can hit.
        Assert.Equal(RoadmapGeometry.MinBarWidthRem, new RoadmapGeometry(Q1).WidthFor(On(1, 1), On(1, 1)));
    }

    [Fact]
    public void The_track_is_as_long_as_the_window_it_draws()
    {
        Assert.Equal(90, DayPerRem.TrackWidthRem);
        Assert.Equal(90, Q1.TotalDays);
    }

    [Theory]
    [InlineData(0, 0, 1.375, 0.625)]
    [InlineData(1, 2.75, 4.125, 3.375)]
    [InlineData(2, 5.5, 6.875, 6.125)]
    public void Rows_stack_by_their_index_and_a_bar_is_centred_in_the_row_it_sits_on(
        int index, double top, double centre, double barTop)
    {
        Assert.Equal(top, DayPerRem.RowTop(index));
        Assert.Equal(centre, DayPerRem.RowCenter(index));

        // The gap above the bar equals the gap below it: that spare height is
        // the gutter the dependency arrows are routed through.
        Assert.Equal(barTop, DayPerRem.BarTop(index));
        Assert.Equal(barTop - top, top + DayPerRem.RowHeightRem - (barTop + DayPerRem.BarHeightRem));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3.4, 0)]
    [InlineData(3.5, 1)]
    [InlineData(7, 1)]
    [InlineData(10.4, 1)]
    [InlineData(10.5, 2)]
    [InlineData(-3.4, 0)]
    [InlineData(-3.5, -1)]
    [InlineData(-10.5, -2)]
    public void A_drag_counts_in_whole_weeks_and_rounds_to_the_nearest(double deltaRem, int expected) =>
        Assert.Equal(expected, DayPerRem.WeekStepsFor(deltaRem));

    [Fact]
    public void Positions_are_written_with_a_dot_on_a_machine_that_writes_commas()
    {
        // Not hypothetical: this is built on a nl-NL machine. "width: 15,77rem"
        // is not a length, so a chart formatted in the reader's culture would
        // collapse against the left edge there and nowhere else on earth.
        var reader = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");

        try
        {
            using var context = new BunitContext();

            var view = Chart(context);
            var track = view.Find(".roadmap-timeline__track").GetAttribute("style")!;

            // The premise, stated so this cannot quietly pass on a build with no
            // culture data to be wrong about.
            Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            Assert.Equal("12.5", RoadmapGeometry.N(12.5));
            Assert.Contains(".", track, StringComparison.Ordinal);
            Assert.DoesNotContain(",", track, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = reader;
        }
    }

    // --- Snapping to a week ---------------------------------------------------

    [Theory]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(7, 5)]
    [InlineData(8, 5)]
    [InlineData(9, 12)]
    [InlineData(10, 12)]
    [InlineData(11, 12)]
    public void A_date_snaps_to_whichever_week_boundary_is_nearest(int day, int expected)
    {
        // Nearest, not floor. Flooring would mean a bar dragged five days
        // forward moved nothing at all, which reads as a broken drag rather
        // than a rounded one. The week splits three days back, four forward.
        Assert.Equal(On(1, expected), RoadmapWindow.SnapToWeek(On(1, day), DayOfWeek.Monday));
    }

    [Fact]
    public void Which_day_a_week_starts_on_moves_every_boundary_with_it()
    {
        // The same Thursday, snapped against two different weeks: back to
        // Monday where the week opens on Monday, forward to Sunday where it
        // opens on Sunday.
        Assert.Equal(On(1, 5), RoadmapWindow.SnapToWeek(On(1, 8), DayOfWeek.Monday));
        Assert.Equal(On(1, 11), RoadmapWindow.SnapToWeek(On(1, 8), DayOfWeek.Sunday));
    }

    [Theory]
    [InlineData(5, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 11)]
    [InlineData(11, 11)]
    [InlineData(12, 11)]
    public void An_end_date_snaps_to_the_last_day_of_a_week_rather_than_the_first(int day, int expected)
    {
        // An end is the last day inclusive, so it belongs on the Sunday. Snapped
        // like a start it would land on a Monday and draw a bar that stops the
        // moment its final week opens.
        var snapped = RoadmapChange.SnapWeekEnd(On(1, day), DayOfWeek.Monday);

        Assert.Equal(On(1, expected), snapped);
        Assert.Equal(DayOfWeek.Sunday, snapped.DayOfWeek);
    }

    // --- What a gesture does to a bar -----------------------------------------

    [Fact]
    public void A_move_carries_the_bar_s_length_with_it()
    {
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 5), On(1, 16));

        var change = RoadmapChange.For(bar, RoadmapDrag.Move, 2, "build", DayOfWeek.Monday);

        Assert.NotNull(change);
        Assert.Equal(On(1, 19), change.Start);
        Assert.Equal(On(1, 30), change.End);
        Assert.Equal(bar.Days, change.ApplyTo(bar).Days);
        Assert.Equal(RoadmapDrag.Move, change.Kind);
    }

    [Fact]
    public void A_bar_moved_from_mid_week_lands_on_a_boundary_and_still_runs_as_long()
    {
        // Three days of work starting on a Wednesday, dragged a week: it lands
        // on the Monday, and it is still three days of work. The length is
        // carried rather than recomputed from the snapped start.
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 7), On(1, 9));

        var change = RoadmapChange.For(bar, RoadmapDrag.Move, 1, "build", DayOfWeek.Monday);

        Assert.NotNull(change);
        Assert.Equal(On(1, 12), change.Start);
        Assert.Equal(On(1, 14), change.End);
        Assert.Equal(3, change.ApplyTo(bar).Days);
    }

    [Theory]
    [InlineData(RoadmapDrag.Move)]
    [InlineData(RoadmapDrag.ResizeStart)]
    [InlineData(RoadmapDrag.ResizeEnd)]
    public void A_gesture_that_travelled_no_whole_week_reports_nothing(RoadmapDrag kind)
    {
        // Otherwise picking a bar up and putting it back down would snap its
        // dates to the nearest week and push a no-op onto the host's undo stack.
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 7), On(1, 9));

        Assert.Null(RoadmapChange.For(bar, kind, 0, "build", DayOfWeek.Monday));
    }

    [Theory]
    [InlineData(RoadmapDrag.Move)]
    [InlineData(RoadmapDrag.ResizeStart)]
    [InlineData(RoadmapDrag.ResizeEnd)]
    public void A_locked_bar_refuses_every_gesture(RoadmapDrag kind)
    {
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 5), On(1, 16), Locked: true);

        Assert.Null(RoadmapChange.For(bar, kind, 3, "ship", DayOfWeek.Monday));
    }

    [Fact]
    public void A_bar_dropped_on_another_row_without_travelling_in_time_keeps_its_dates()
    {
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 7), On(1, 9));

        var change = RoadmapChange.For(bar, RoadmapDrag.Move, 0, "ship", DayOfWeek.Monday);

        Assert.NotNull(change);
        Assert.Equal("ship", change.RowId);
        Assert.Equal(bar.Start, change.Start);
        Assert.Equal(bar.End, change.End);
    }

    [Fact]
    public void Resizing_one_edge_leaves_the_other_where_it_was()
    {
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 12), On(1, 25));

        var start = RoadmapChange.For(bar, RoadmapDrag.ResizeStart, -1, null, DayOfWeek.Monday);
        var end = RoadmapChange.For(bar, RoadmapDrag.ResizeEnd, 1, null, DayOfWeek.Monday);

        Assert.NotNull(start);
        Assert.Equal(On(1, 5), start.Start);
        Assert.Equal(bar.End, start.End);
        Assert.Equal(RoadmapDrag.ResizeStart, start.Kind);

        Assert.NotNull(end);
        Assert.Equal(bar.Start, end.Start);
        Assert.Equal(On(2, 1), end.End);
        Assert.Equal(RoadmapDrag.ResizeEnd, end.Kind);
    }

    [Fact]
    public void An_edge_pulled_past_the_opposite_edge_clamps_instead_of_inverting()
    {
        var bar = new RoadmapBar("alpha", "build", "Alpha", On(1, 5), On(1, 21));

        var start = RoadmapChange.For(bar, RoadmapDrag.ResizeStart, 5, null, DayOfWeek.Monday);
        var end = RoadmapChange.For(
            new RoadmapBar("beta", "ship", "Beta", On(1, 19), On(1, 25)),
            RoadmapDrag.ResizeEnd, -3, null, DayOfWeek.Monday);

        // The shortest bar the grid allows, rather than a bar drawn backwards.
        Assert.NotNull(start);
        Assert.Equal(On(1, 19), start.Start);
        Assert.Equal(bar.End, start.End);
        Assert.True(start.Start <= start.End);

        Assert.NotNull(end);
        Assert.Equal(On(1, 19), end.Start);
        Assert.Equal(On(1, 19), end.End);
    }

    [Fact]
    public void An_edge_that_clamps_back_to_where_it_started_reports_nothing()
    {
        // A clamp that lands on the date the edge already had has changed
        // nothing, and saying so anyway would report an edit the reader's
        // gesture did not make.
        var stub = new RoadmapBar("alpha", "build", "Alpha", On(1, 5), On(1, 8));
        var tail = new RoadmapBar("beta", "ship", "Beta", On(1, 8), On(1, 11));

        Assert.Null(RoadmapChange.For(stub, RoadmapDrag.ResizeStart, 1, null, DayOfWeek.Monday));
        Assert.Null(RoadmapChange.For(tail, RoadmapDrag.ResizeEnd, -1, null, DayOfWeek.Monday));
    }

    // --- Filtering ------------------------------------------------------------

    [Fact]
    public void Every_bar_is_on_the_chart_until_something_is_filtered()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        Assert.Equal(["alpha", "beta", "gamma"], DrawnBars(view));
    }

    [Fact]
    public void The_filter_bar_offers_one_control_per_facet_the_bars_actually_carry()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        // In the order the bars first mention them, not alphabetically: that is
        // the order the caller thought of them in. The names are the caller's
        // too — this component has never heard of a tag or a repository.
        Assert.Equal(
            ["Tag", "Repo"],
            view.FindAll("[data-testid='rm-filters'] .field__label").Select(label => label.TextContent));

        view.Find("[data-testid='rm-filter-tag']");
        view.Find("[data-testid='rm-filter-repo']");
    }

    [Fact]
    public void Bars_with_nothing_to_filter_on_get_no_filter_bar_at_all()
    {
        using var context = new BunitContext();

        var view = Chart(context, [new RoadmapBar("solo", "build", "Solo", On(1, 5), On(1, 16))]);

        Assert.Empty(view.FindAll("[data-testid='rm-filters']"));
    }

    [Fact]
    public void A_host_can_turn_the_filter_bar_off_even_where_there_is_something_to_filter()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(timeline => timeline.ShowFilters, false));

        Assert.Empty(view.FindAll("[data-testid='rm-filters']"));
        Assert.Equal(["alpha", "beta", "gamma"], DrawnBars(view));
    }

    [Fact]
    public void Choosing_a_facet_value_leaves_only_the_bars_that_carry_it()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        view.Find("[data-testid='rm-filter-tag'] input").Focus();
        view.FindAll("[data-testid='rm-filter-tag'] [role='option']")
            .Single(option => option.TextContent == "design")
            .Click();

        // beta is tagged something else; gamma carries no Tag facet at all, and
        // a bar that cannot answer the question is not an answer to it.
        Assert.Equal(["alpha"], DrawnBars(view));
    }

    [Fact]
    public void Clearing_the_filters_puts_every_bar_back()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        view.Find("[data-testid='rm-filter-tag'] input").Focus();
        view.FindAll("[data-testid='rm-filter-tag'] [role='option']")
            .Single(option => option.TextContent == "infra")
            .Click();

        Assert.Equal(["beta"], DrawnBars(view));

        view.Find("[data-testid='rm-filter-clear']").Click();

        Assert.Equal(["alpha", "beta", "gamma"], DrawnBars(view));
    }

    [Fact]
    public void What_is_being_filtered_on_is_reported_so_a_host_can_mirror_it()
    {
        using var context = new BunitContext();
        IReadOnlyDictionary<string, IReadOnlyList<string>>? reported = null;

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.OnFilterChanged,
            (IReadOnlyDictionary<string, IReadOnlyList<string>> values) => reported = values));

        view.Find("[data-testid='rm-filter-tag'] input").Focus();
        view.FindAll("[data-testid='rm-filter-tag'] [role='option']")
            .Single(option => option.TextContent == "design")
            .Click();

        Assert.NotNull(reported);
        Assert.Equal(["design"], reported["Tag"]);
    }

    // --- Dependency arrows ----------------------------------------------------

    [Fact]
    public void An_arrow_is_drawn_for_a_link_whose_two_ends_are_both_on_the_chart()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.Links,
            new RoadmapLink[] { new("alpha", "beta") }));

        var path = Assert.Single(view.FindAll("[data-testid='rm-links'] .roadmap-timeline__link"));

        Assert.StartsWith("M ", path.GetAttribute("d")!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_naming_something_that_is_not_in_the_plan_draws_nothing()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.Links,
            new RoadmapLink[] { new("alpha", "nowhere") }));

        // Not a shorter arrow, not an arrow to the edge: no arrow. A line to
        // empty space is a claim about a relationship nobody can check.
        Assert.Empty(view.FindAll("[data-testid='rm-links']"));
    }

    [Fact]
    public void A_link_to_a_bar_filtered_off_the_chart_is_dropped_with_it()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.Links,
            new RoadmapLink[] { new("alpha", "beta") }));

        Assert.Single(view.FindAll(".roadmap-timeline__link"));

        view.Find("[data-testid='rm-filter-tag'] input").Focus();
        view.FindAll("[data-testid='rm-filter-tag'] [role='option']")
            .Single(option => option.TextContent == "design")
            .Click();

        Assert.Equal(["alpha"], DrawnBars(view));
        Assert.Empty(view.FindAll("[data-testid='rm-links']"));
    }

    [Fact]
    public void A_milestone_can_be_either_end_of_a_dependency()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.Links,
            new RoadmapLink[] { new("alpha", "launch"), new("launch", "gamma") }));

        view.Find("[data-testid='rm-milestone-launch']");

        Assert.Equal(2, view.FindAll(".roadmap-timeline__link").Count);
    }

    [Fact]
    public void The_badge_on_a_bar_counts_the_arrows_that_were_actually_drawn()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.Links,
            new RoadmapLink[] { new("alpha", "beta"), new("gamma", "beta"), new("ghost", "beta") }));

        // Three links point at beta and one of them names nothing. The badge and
        // the arrows on screen are the same count, because a reader who sees "3"
        // and can find two lines has been told the chart is missing one.
        Assert.Equal(2, view.FindAll(".roadmap-timeline__link").Count);
        Assert.Equal("2", view.Find("[data-testid='rm-bar-beta'] .roadmap-bar__badge").TextContent);
        Assert.Contains("2 dependencies", view.Find("[data-testid='rm-bar-beta']").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, 2, 1)]
    [InlineData(0.9, 3, 2)]
    public void A_link_with_room_in_front_of_it_is_an_elbow_and_one_without_is_a_detour(
        double endX, int alongs, int downs)
    {
        // Which of the two shapes is drawn is itself information: the detour is
        // the reader's cue that the dependent work starts before the thing it
        // waits for finishes. Counted as commands rather than compared as text,
        // because the coordinates are free to be tuned and the shape is not.
        var path = new RoadmapGeometry(Q1).LinkPath(0, 0, endX, 0);

        Assert.Equal(alongs, path.Count(command => command == 'H'));
        Assert.Equal(downs, path.Count(command => command == 'V'));
    }

    [Fact]
    public void A_link_doubling_back_along_one_row_is_routed_under_the_bar_not_through_it()
    {
        var geometry = new RoadmapGeometry(Q1);
        var centre = geometry.RowCenter(0);

        var path = geometry.LinkPath(10, centre, 2, centre);
        var channel = double.Parse(path.Split(' ')[6], CultureInfo.InvariantCulture);

        // Below the bar's centre line, still inside the row: a return line drawn
        // across the bar itself would read as a strikethrough.
        Assert.True(channel > centre);
        Assert.True(channel < geometry.RowHeightRem);
    }

    // --- The keyboard half of a drag ------------------------------------------

    [Fact]
    public void Space_picks_a_bar_up_and_says_so()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        view.Find("[data-testid='rm-bar-alpha'] .roadmap-bar__body").KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.Contains("Grabbed Alpha", Announcement(view), StringComparison.Ordinal);
        Assert.Contains("roadmap-bar--grabbed", view.Find("[data-testid='rm-bar-alpha']").ClassName!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arrow_key_then_space_drops_the_bar_a_week_later()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        var bar = view.Find("[data-testid='rm-bar-alpha'] .roadmap-bar__body");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        bar.KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.NotNull(reported);
        Assert.Equal("alpha", reported.BarId);
        Assert.Equal("build", reported.RowId);
        Assert.Equal(On(1, 12), reported.Start);
        Assert.Equal(On(1, 23), reported.End);
        Assert.Equal(RoadmapDrag.Move, reported.Kind);
        Assert.Contains("Dropped Alpha", Announcement(view), StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_puts_the_bar_back_and_tells_the_host_nothing()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        var bar = view.Find("[data-testid='rm-bar-alpha'] .roadmap-bar__body");
        var before = view.Find("[data-testid='rm-bar-alpha']").GetAttribute("style");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        bar.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(reported);
        Assert.Contains("Move cancelled", Announcement(view), StringComparison.Ordinal);
        Assert.Equal(before, view.Find("[data-testid='rm-bar-alpha']").GetAttribute("style"));
    }

    [Fact]
    public void A_bar_walked_onto_another_row_of_bars_arrives_with_its_dates_untouched()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        var bar = view.Find("[data-testid='rm-bar-alpha'] .roadmap-bar__body");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        bar.KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.NotNull(reported);
        Assert.Equal("ship", reported.RowId);
        Assert.Equal(On(1, 5), reported.Start);
        Assert.Equal(On(1, 16), reported.End);
    }

    [Fact]
    public void A_bar_walked_onto_a_milestones_row_is_refused_rather_than_quietly_undone()
    {
        using var context = new BunitContext();
        RoadmapChange? reported = null;

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.OnBarChanged,
            (RoadmapChange change) => reported = change));

        var bar = view.Find("[data-testid='rm-bar-alpha'] .roadmap-bar__body");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        bar.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // Said while the bar is still in the air, and it has not moved: the
        // reader is told they cannot, rather than allowed to drop it there and
        // watch it spring back.
        Assert.Contains("milestones", Announcement(view), StringComparison.Ordinal);
        view.Find("[data-testid='rm-row-build'] [data-testid='rm-bar-alpha']");

        bar.KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.Null(reported);
    }

    // --- What the timeline declines to invent ---------------------------------

    [Fact]
    public void A_plan_with_no_groups_says_so_instead_of_ruling_an_empty_grid()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, Array.Empty<RoadmapGroup>())
            .Add(timeline => timeline.Bars, Work)
            .Add(timeline => timeline.Window, Q1)
            .Add(timeline => timeline.EmptyMessage, "Nothing planned.")
            .Add(timeline => timeline.TestId, "rm"));

        Assert.Equal("Nothing planned.", view.Find("[data-testid='rm-empty']").TextContent);
        Assert.Empty(view.FindAll(".roadmap-timeline__track"));
    }

    [Fact]
    public void A_group_s_colour_is_handed_over_as_it_stands_and_nothing_else_is_tinted()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        Assert.Contains(
            "--roadmap-group-color: #3366ff",
            view.Find("[data-testid='rm-row-build']").GetAttribute("style")!,
            StringComparison.Ordinal);

        // The group that named no colour gets none — not a seventh hue picked
        // for it. The library's palette guide forbids a second semantic palette,
        // which is exactly what choosing six separable band colours would be.
        Assert.DoesNotContain(
            "color",
            view.Find("[data-testid='rm-row-moments']").GetAttribute("style")!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_bar_is_drawn_from_its_dates_and_from_nothing_else()
    {
        using var context = new BunitContext();

        var view = Chart(context, extra: parameters => parameters.Add(
            timeline => timeline.QuarterWidth,
            RoadmapWindow.NominalQuarterDays));

        // A day to the rem: Alpha starts four days into the window and runs
        // twelve of them.
        Assert.Equal("left: 4rem; width: 12rem", view.Find("[data-testid='rm-bar-alpha']").GetAttribute("style"));
    }

    [Fact]
    public void A_shade_the_stylesheet_does_not_define_falls_back_to_one_it_does()
    {
        using var context = new BunitContext();

        var view = Chart(context, [new RoadmapBar("solo", "build", "Solo", On(1, 5), On(1, 16), Shade: 9)]);

        // The whole class list, deliberately: a bar's appearance is its group's
        // colour and a shade step of it, and an out-of-range shade lands on the
        // lightest defined step rather than on a class nothing styles.
        Assert.Equal("roadmap-bar roadmap-bar--shade-3", view.Find("[data-testid='rm-bar-solo']").ClassName);
    }
}
