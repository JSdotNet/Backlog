namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a bar and a milestone say on hover: a short report, one fact to a line,
/// rather than one run-on line a reader has to parse. The accessible name says the
/// same facts as a sentence, because a line break is not something a screen reader
/// pauses on.
/// </summary>
public sealed class RoadmapTooltipTests
{
    private static readonly RoadmapWindow Q1 = new(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

    private static readonly IReadOnlyList<RoadmapGroup> Plan =
    [
        new("delivery", "Delivery", [new RoadmapRow("build", "Build")]),
        new("dates", "Dates", [new RoadmapRow("moments", "Moments", RoadmapRowKind.Milestones)])
    ];

    private static readonly IReadOnlyList<RoadmapStep> Steps =
    [
        new("parse", "Parse", 3, RoadmapStepTone.Done, "Done"),
        new("draw", "Draw", 8, RoadmapStepTone.InProgress, "In progress"),
        new("docs", "Docs", null, RoadmapStepTone.Ready, "Ready")
    ];

    private static readonly IReadOnlyList<RoadmapBar> Work =
    [
        new("alpha", "build", "Alpha", On(1, 5), On(1, 16),
            Detail: "High priority\ntagged sync\nwaits for 2 things", Locked: true, Steps: Steps),
        new("beta", "build", "Beta", On(1, 19), On(1, 30))
    ];

    private static readonly IReadOnlyList<RoadmapMilestone> Moments =
    [
        new("launch", "moments", "Launch", On(1, 26), RoadmapMarker.Star, "Release\nread against the whole plan")
    ];

    private static DateOnly On(int month, int day) => new(2026, month, day);

    private static IRenderedComponent<RoadmapTimeline> Chart(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, Plan)
            .Add(timeline => timeline.Bars, Work)
            .Add(timeline => timeline.Milestones, Moments)
            .Add(timeline => timeline.Window, Q1)
            // Fixed, so the assertions do not depend on the machine's culture.
            .Add(timeline => timeline.DateLabel, date => date.ToString("yyyy-MM-dd"))
            .Add(timeline => timeline.TestId, "rm"));
    }

    [Fact]
    public void A_bar_tooltip_is_a_report_with_one_fact_to_a_line()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var title = view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body").GetAttribute("title");

        Assert.Equal(
            string.Join('\n',
                "Alpha",
                "2026-01-05 to 2026-01-16",
                "3 of 11 points done, 1 unestimated",
                "High priority",
                "tagged sync",
                "waits for 2 things",
                "Fixed, cannot be moved"),
            title);
    }

    [Fact]
    public void A_plain_bar_tooltip_says_only_what_it_has()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        Assert.Equal(
            "Beta\n2026-01-19 to 2026-01-30",
            view.Find("[data-roadmap-bar='beta'] .roadmap-bar__body").GetAttribute("title"));
    }

    [Fact]
    public void The_accessible_name_reads_the_detail_lines_as_one_sentence()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var spoken = view.Find("[data-roadmap-bar='alpha'] .roadmap-bar__body .sr-only").TextContent;

        Assert.DoesNotContain('\n', spoken);
        Assert.Contains("High priority, tagged sync, waits for 2 things", spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void A_milestone_tooltip_is_a_report_and_its_accessible_name_a_sentence()
    {
        using var context = new BunitContext();
        var view = Chart(context);

        var marker = view.Find("[data-testid='rm-milestone-launch']");

        Assert.Equal("Launch\n2026-01-26\nRelease\nread against the whole plan", marker.GetAttribute("title"));

        var spoken = marker.QuerySelector(".sr-only")!.TextContent;
        Assert.DoesNotContain('\n', spoken);
        Assert.Contains("Release, read against the whole plan.", spoken, StringComparison.Ordinal);
    }
}
