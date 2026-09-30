namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Adding in place: a double-click on an empty stretch of row proposes that row and
/// the week under the pointer, and a bar with nothing behind it yet is drawn as
/// intent rather than as work. The pointer half is the script's; what is pinned here
/// is the arithmetic that turns a distance back into a week, which rows offer the
/// gesture at all, and what reaches the host.
/// </summary>
public sealed class RoadmapTimelineSlotTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    // 1 January 2026 is a Thursday; the Mondays are the 5th, 12th, 19th and 26th.
    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build")], "#3366ff"),
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)])
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "build", "Alpha", On(1, 5), On(1, 16)),
        new("guess", "build", "Guess", On(2, 2), On(2, 13), Tentative: true)
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    private static IRenderedComponent<RoadmapTimeline> Chart(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<RoadmapTimeline>>? extra = null)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters =>
        {
            parameters
                .Add(timeline => timeline.Groups, Plan)
                .Add(timeline => timeline.Bars, Work)
                .Add(timeline => timeline.Window, Q1)
                .Add(timeline => timeline.TestId, "rm");

            extra?.Invoke(parameters);
        });
    }

    // --- A distance read back as a date ---------------------------------------

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 21)]
    [InlineData(2, 28)]
    [InlineData(3, 31)]
    public void A_date_read_back_from_where_it_is_drawn_is_the_same_date(int month, int day)
    {
        var geometry = new RoadmapGeometry(Q1);

        Assert.Equal(On(month, day), geometry.DateAt(geometry.XFor(On(month, day))));
        Assert.Equal(On(month, day), geometry.DateAt(geometry.XFor(On(month, day).AddDays(1)) - 0.01));
    }

    [Fact]
    public void A_date_read_back_on_a_graduated_axis_walks_its_columns()
    {
        var today = On(2, 11);
        var window = RoadmapWindow.Graduated([On(1, 5), On(9, 30)], today, DayOfWeek.Monday);
        var geometry = new RoadmapGeometry(window);

        foreach (var date in new[] { today, On(2, 20), On(4, 15), On(9, 1) })
        {
            Assert.Equal(date, geometry.DateAt(geometry.XFor(date) + 0.001));
        }
    }

    // --- Proposing a slot ------------------------------------------------------

    [Fact]
    public async Task A_double_click_on_a_row_proposes_that_row_and_the_first_day_of_its_week()
    {
        using var context = new BunitContext();

        var slots = new List<RoadmapSlot>();
        var view = Chart(context, parameters => parameters.Add(timeline => timeline.OnSlotActivated, slot => slots.Add(slot)));

        // A Wednesday: the gesture snaps to the week, like every gesture here.
        await view.InvokeAsync(() => view.Instance.SlotActivated("build", new RoadmapGeometry(Q1).XFor(On(1, 21)) + 0.1));

        Assert.Equal([new RoadmapSlot("build", On(1, 19), RoadmapRowKind.Bars)], slots);
    }

    [Fact]
    public async Task A_milestones_row_says_so_in_the_slot_it_proposes()
    {
        using var context = new BunitContext();

        var slots = new List<RoadmapSlot>();
        var view = Chart(context, parameters => parameters.Add(timeline => timeline.OnSlotActivated, slot => slots.Add(slot)));

        await view.InvokeAsync(() => view.Instance.SlotActivated("moments", new RoadmapGeometry(Q1).XFor(On(2, 3))));

        Assert.Equal(RoadmapRowKind.Milestones, Assert.Single(slots).Kind);
    }

    [Fact]
    public async Task A_row_the_chart_does_not_draw_proposes_nothing()
    {
        using var context = new BunitContext();

        var slots = new List<RoadmapSlot>();
        var view = Chart(context, parameters => parameters.Add(timeline => timeline.OnSlotActivated, slot => slots.Add(slot)));

        await view.InvokeAsync(() => view.Instance.SlotActivated("nowhere", 3));

        Assert.Empty(slots);
    }

    [Fact]
    public void Rows_offer_the_gesture_only_when_the_host_listens()
    {
        using var context = new BunitContext();

        var silent = Chart(context);

        Assert.Empty(silent.FindAll("[data-roadmap-slot]"));
        Assert.DoesNotContain("roadmap-timeline--insertable", silent.Find(".roadmap-timeline").ClassName);

        var listening = Chart(context, parameters => parameters.Add(timeline => timeline.OnSlotActivated, _ => { }));

        Assert.Equal("build", listening.Find("[data-testid='rm-row-build']").GetAttribute("data-roadmap-slot"));
        Assert.Equal("moments", listening.Find("[data-testid='rm-row-moments']").GetAttribute("data-roadmap-slot"));
        Assert.Contains("roadmap-timeline--insertable", listening.Find(".roadmap-timeline").ClassName);
    }

    // --- Intent drawn apart from work -----------------------------------------

    [Fact]
    public void A_tentative_bar_is_drawn_apart_and_the_rest_are_not()
    {
        using var context = new BunitContext();

        var view = Chart(context);

        Assert.Contains("roadmap-bar--tentative", view.Find("[data-testid='rm-bar-guess']").ClassName);
        Assert.DoesNotContain("roadmap-bar--tentative", view.Find("[data-testid='rm-bar-alpha']").ClassName);
    }
}
