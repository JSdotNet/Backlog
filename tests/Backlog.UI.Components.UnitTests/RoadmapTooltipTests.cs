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

    // --- Overdue: drawn as a warning, said by the caller's words ---------------

    private static IRenderedComponent<RoadmapTimeline> LateChart(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IReadOnlyList<RoadmapBar> bars =
        [
            new("late", "build", "Late", On(1, 5), On(1, 9), Detail: "Overdue: ended 9 Jan 2026\nMedium priority", Overdue: true),
            new("guess", "build", "Guess", On(1, 12), On(1, 16), Detail: "Overdue: ended 16 Jan 2026\nno task yet", Tentative: true, Overdue: true),
            new("fine", "build", "Fine", On(1, 19), On(1, 30)),
            new("behind", "build", "Behind", On(2, 2), On(2, 6),
                Detail: "Overdue: ended 6 Feb 2026\nHigh priority", Steps: Steps, Overdue: true)
        ];

        return context.Render<RoadmapTimeline>(parameters => parameters
            .Add(timeline => timeline.Groups, Plan)
            .Add(timeline => timeline.Bars, bars)
            .Add(timeline => timeline.Window, Q1)
            .Add(timeline => timeline.DateLabel, date => date.ToString("yyyy-MM-dd"))
            .Add(timeline => timeline.TestId, "rm"));
    }

    [Fact]
    public void An_overdue_bar_wears_the_warning_and_a_marker_a_screen_reader_skips()
    {
        using var context = new BunitContext();
        var view = LateChart(context);

        var late = view.Find("[data-roadmap-bar='late']");
        Assert.Contains("roadmap-bar--overdue", late.ClassList);

        var marker = late.QuerySelector(".roadmap-bar__overdue");
        Assert.NotNull(marker);
        Assert.Equal("true", marker.GetAttribute("aria-hidden"));

        var fine = view.Find("[data-roadmap-bar='fine']");
        Assert.DoesNotContain("roadmap-bar--overdue", fine.ClassList);
        Assert.Null(fine.QuerySelector(".roadmap-bar__overdue"));
    }

    [Fact]
    public void An_overdue_bar_says_so_in_its_tooltip_and_its_accessible_name_in_the_callers_words()
    {
        using var context = new BunitContext();
        var view = LateChart(context);

        var body = view.Find("[data-roadmap-bar='late'] .roadmap-bar__body");

        Assert.Equal("Late\n2026-01-05 to 2026-01-09\nOverdue: ended 9 Jan 2026\nMedium priority", body.GetAttribute("title"));
        Assert.Contains("Overdue: ended 9 Jan 2026, Medium priority", body.QuerySelector(".sr-only")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The warning is the one line that asks the reader to act, so on a bar
    /// with progress it still comes straight after the dates — ahead of the "points
    /// done" line the tooltip otherwise puts first.</summary>
    [Fact]
    public void An_overdue_bar_with_progress_says_overdue_before_its_progress()
    {
        using var context = new BunitContext();
        var view = LateChart(context);

        var body = view.Find("[data-roadmap-bar='behind'] .roadmap-bar__body");

        Assert.Equal(
            string.Join('\n',
                "Behind",
                "2026-02-02 to 2026-02-06",
                "Overdue: ended 6 Feb 2026",
                "3 of 11 points done, 1 unestimated",
                "High priority"),
            body.GetAttribute("title"));
    }

    [Fact]
    public void A_tentative_bar_that_is_overdue_keeps_its_outline_and_wears_the_warning_too()
    {
        using var context = new BunitContext();
        var view = LateChart(context);

        var guess = view.Find("[data-roadmap-bar='guess']");

        Assert.Contains("roadmap-bar--tentative", guess.ClassList);
        Assert.Contains("roadmap-bar--overdue", guess.ClassList);
    }
}
